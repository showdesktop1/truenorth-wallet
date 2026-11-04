using System;
using System.IO;
using System.Text.Json;

namespace TrueNorthWallet;

public class WalletSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 9554;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public bool RememberPassword { get; set; } = false;
    public string CookiePath { get; set; } = "";
    public string Wallet { get; set; } = "";
    public string Unit { get; set; } = "NORTH";

    private static string FilePath
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrueNorthWallet");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    public static WalletSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                {
                var loaded = JsonSerializer.Deserialize<WalletSettings>(File.ReadAllText(FilePath)) ?? new WalletSettings();
                if (loaded.Unit == "mie") loaded.Unit = "miettes";
                return loaded;
            }
        }
        catch { }
        return new WalletSettings();
    }

    public void Save()
    {
        var copy = new WalletSettings
        {
            Host = Host,
            Port = Port,
            User = User,
            Password = RememberPassword ? Password : "",
            RememberPassword = RememberPassword,
            CookiePath = CookiePath,
            Wallet = Wallet,
            Unit = Unit
        };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true }));
    }
}

