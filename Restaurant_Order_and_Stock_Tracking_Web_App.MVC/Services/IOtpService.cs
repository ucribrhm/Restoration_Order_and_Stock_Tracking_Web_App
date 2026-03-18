// ============================================================================
//  Services/IOtpService.cs + OtpService.cs
//
//  SPRINT 5 — [OTP-1] OTP Üretimi, Doğrulama ve Lockout
//
//  Kararlar:
//    • IMemoryCache — migration yok, tek instance
//    • SHA-256 hash — düz metin saklanmaz
//    • TTL: 10 dakika
//    • Cooldown: 60 saniye (tekrar gönder butonu)
//    • Lockout: 3 yanlış deneme → 15 dakika bloke
//
//  Cache key şeması:
//    otp:{purpose}:{email}        → OtpRecord (hash + attemptCount + cooldownUntil)
//    otp_locked:{purpose}:{email} → "1"  (15 dk bloke)
// ============================================================================

using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services
{
    // ── Enum: OTP amacı ───────────────────────────────────────────────────────
    public enum OtpPurpose
    {
        Register,
        ResetPassword
    }

    // ── Cache'e yazılan kayıt ─────────────────────────────────────────────────
    public record OtpRecord(
        string CodeHash,       // SHA-256 hex
        int AttemptCount,   // Yanlış deneme sayacı
        DateTime CooldownUntil,  // "Tekrar gönder" butonu ne zaman aktif
        DateTime ExpiresAt       // OTP'nin son kullanma zamanı
    );

    // ── Interface ────────────────────────────────────────────────────────────
    public interface IOtpService
    {
        /// <summary>6 haneli OTP üretir, cache'e yazar ve kodu string olarak döner.</summary>
        string Generate(string email, OtpPurpose purpose);

        /// <summary>
        /// Girilen kodu doğrular.
        /// Dönen değerler: Success | InvalidCode | Expired | Locked | CooldownActive
        /// </summary>
        OtpVerifyResult Verify(string email, OtpPurpose purpose, string enteredCode);

        /// <summary>OTP'yi cache'ten siler. Doğrulama başarılı olunca çağrılır.</summary>
        void Consume(string email, OtpPurpose purpose);

        /// <summary>Cooldown süresi dolmuş mu? "Tekrar gönder" butonu için.</summary>
        bool IsCooldownActive(string email, OtpPurpose purpose);

        /// <summary>Kalan cooldown saniyesi. 0 ise cooldown bitti.</summary>
        int GetCooldownSeconds(string email, OtpPurpose purpose);
    }

    public enum OtpVerifyResult
    {
        Success,
        InvalidCode,   // Yanlış kod — AttemptCount arttı
        Expired,       // TTL dolmuş
        Locked,        // 3 yanlış deneme — 15 dk bloke
        NotFound       // Cache'te kayıt yok
    }

    // ── Implementasyon ────────────────────────────────────────────────────────
    public class OtpService : IOtpService
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<OtpService> _logger;

        private static readonly TimeSpan OtpTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan CooldownTtl = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan LockoutTtl = TimeSpan.FromMinutes(15);
        private const int MaxAttempts = 3;

        public OtpService(IMemoryCache cache, ILogger<OtpService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        // ── Generate ─────────────────────────────────────────────────────────
        public string Generate(string email, OtpPurpose purpose)
        {
            // 6 haneli kriptografik rastgele kod
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000)
                                            .ToString("D6");

            var record = new OtpRecord(
                CodeHash: HashCode(code),
                AttemptCount: 0,
                CooldownUntil: DateTime.UtcNow.Add(CooldownTtl),
                ExpiresAt: DateTime.UtcNow.Add(OtpTtl)
            );

            _cache.Set(OtpKey(email, purpose), record,
                new MemoryCacheEntryOptions
                {
                    Size = 1,
                    AbsoluteExpiration = DateTimeOffset.UtcNow.Add(OtpTtl)
                });

            _logger.LogInformation("[OTP] Üretildi. Purpose: {Purpose} | Email: {Email}", purpose, email);
            return code;
        }

        // ── Verify ───────────────────────────────────────────────────────────
        public OtpVerifyResult Verify(string email, OtpPurpose purpose, string enteredCode)
        {
            // Lockout kontrolü
            if (_cache.TryGetValue(LockKey(email, purpose), out _))
            {
                _logger.LogWarning("[OTP] Kilitli hesaba giriş denemesi. Email: {Email}", email);
                return OtpVerifyResult.Locked;
            }

            if (!_cache.TryGetValue(OtpKey(email, purpose), out OtpRecord? record) || record is null)
                return OtpVerifyResult.NotFound;

            // TTL kontrolü (cache TTL'ye ek yazılım kontrolü)
            if (DateTime.UtcNow > record.ExpiresAt)
            {
                _cache.Remove(OtpKey(email, purpose));
                return OtpVerifyResult.Expired;
            }

            // Kod doğrulama
            if (record.CodeHash != HashCode(enteredCode))
            {
                var newCount = record.AttemptCount + 1;

                if (newCount >= MaxAttempts)
                {
                    // Lockout — 15 dakika bloke
                    _cache.Set(LockKey(email, purpose), "1",
                new MemoryCacheEntryOptions
                {
                    Size = 1,
                    AbsoluteExpiration = DateTimeOffset.UtcNow.Add(LockoutTtl)
                });
                    _cache.Remove(OtpKey(email, purpose));

                    _logger.LogWarning("[OTP] Lockout. Email: {Email} | Purpose: {Purpose}", email, purpose);
                    return OtpVerifyResult.Locked;
                }

                // Deneme sayacını artır
                var updated = record with { AttemptCount = newCount };
                _cache.Set(OtpKey(email, purpose), updated,
                new MemoryCacheEntryOptions
                {
                    Size = 1,
                    AbsoluteExpiration = new DateTimeOffset(record.ExpiresAt)
                });

                _logger.LogWarning("[OTP] Yanlış kod. Email: {Email} | Deneme: {Count}/{Max}",
                    email, newCount, MaxAttempts);
                return OtpVerifyResult.InvalidCode;
            }

            // Başarılı — tüket
            _logger.LogInformation("[OTP] Doğrulandı. Email: {Email} | Purpose: {Purpose}", email, purpose);
            return OtpVerifyResult.Success;
        }

        // ── Consume ──────────────────────────────────────────────────────────
        public void Consume(string email, OtpPurpose purpose)
        {
            _cache.Remove(OtpKey(email, purpose));
            _cache.Remove(LockKey(email, purpose));
        }

        // ── Cooldown ─────────────────────────────────────────────────────────
        public bool IsCooldownActive(string email, OtpPurpose purpose)
            => GetCooldownSeconds(email, purpose) > 0;

        public int GetCooldownSeconds(string email, OtpPurpose purpose)
        {
            if (!_cache.TryGetValue(OtpKey(email, purpose), out OtpRecord? record) || record is null)
                return 0;
            var remaining = (record.CooldownUntil - DateTime.UtcNow).TotalSeconds;
            return remaining > 0 ? (int)Math.Ceiling(remaining) : 0;
        }

        // ── Yardımcılar ──────────────────────────────────────────────────────
        private static string OtpKey(string email, OtpPurpose purpose)
            => $"otp:{purpose.ToString().ToLower()}:{email.ToLower()}";

        private static string LockKey(string email, OtpPurpose purpose)
            => $"otp_locked:{purpose.ToString().ToLower()}:{email.ToLower()}";

        private static string HashCode(string code)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
    }
}