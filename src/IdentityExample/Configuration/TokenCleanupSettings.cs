namespace IdentityExample.Configuration;

public class TokenCleanupSettings
{
    public int CleanupIntervalMinutes { get; set; }
    public int ExpiredTokenRetentionHours { get; set; }
}
