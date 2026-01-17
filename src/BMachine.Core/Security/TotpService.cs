using OtpNet;

namespace BMachine.Core.Security;

/// <summary>
/// TOTP (Time-based One-Time Password) service for folder unlock verification.
/// </summary>
public class TotpService
{
    private readonly string _secret;

    public TotpService(string base32Secret)
    {
        _secret = base32Secret;
    }

    /// <summary>
    /// Generates a random Base32 secret for new TOTP setup.
    /// </summary>
    public static string GenerateSecret()
    {
        var key = KeyGeneration.GenerateRandomKey(20);
        return Base32Encoding.ToString(key);
    }

    /// <summary>
    /// Creates a provisioning URI for QR code generation.
    /// </summary>
    public static string GetProvisioningUri(string secret, string accountName = "FolderLocker", string issuer = "BMachine")
    {
        var otp = new Totp(Base32Encoding.ToBytes(secret));
        return $"otpauth://totp/{issuer}:{accountName}?secret={secret}&issuer={issuer}&digits=6&period=30";
    }

    /// <summary>
    /// Verifies a TOTP code.
    /// </summary>
    public bool Verify(string code)
    {
        if (string.IsNullOrWhiteSpace(_secret))
            return false;
            
        var totp = new Totp(Base32Encoding.ToBytes(_secret));
        return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
    }

    /// <summary>
    /// Gets the current TOTP code (for testing/debug only).
    /// </summary>
    public string GetCurrentCode()
    {
        var totp = new Totp(Base32Encoding.ToBytes(_secret));
        return totp.ComputeTotp();
    }
}
