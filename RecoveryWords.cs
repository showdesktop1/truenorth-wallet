using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TrueNorthWallet;

// Restores a wallet from BIP39 recovery words (12 to 24), the way TrueNorth Core
// holds its own keys: the words give a BIP32 master key, and the wallet gets
// the two qrh() descriptors Core uses for P2QRH (doc/p2qrh.md):
//   qrh(xprv/88h/<coin>h/0h/0/*)  receiving, qrh(xprv/88h/<coin>h/0h/1/*)  change
// with coin 1867 on mainnet and 1 on the test networks. Core derives every key
// and address from these itself. These are the same words and keys that
// IronMiner's address setup and the tncoin.xyz address creator produce.
public static class RecoveryWords
{
    private const int MainCoinType = 1867;
    private const int TestCoinType = 1;
    private const uint MainXprvVersion = 0x7e5c6544;
    private const uint TestXprvVersion = 0x04358394;

    // TrueNorth mainnet launch: importing with this timestamp scans the whole chain.
    private const long MainnetLaunch = 1791763200;

    private static readonly BigInteger CurveOrder = BigInteger.Parse(
        "0FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141",
        System.Globalization.NumberStyles.HexNumber);

    private static readonly Lazy<string[]> WordList = new(LoadWordList);
    private static readonly Lazy<Dictionary<string, int>> WordIndex = new(() =>
        WordList.Value.Select((w, i) => (w, i)).ToDictionary(x => x.w, x => x.i));

    private static string[] LoadWordList()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TrueNorthWallet.bip39-english.txt")
            ?? throw new Exception("The recovery word list is missing from this build.");
        using var reader = new StreamReader(stream);
        var words = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length != 2048) throw new Exception("The recovery word list is damaged.");
        return words;
    }

    // Lowercase, single spaces. A word may be given by its first four letters,
    // which is all BIP39 needs to tell the English words apart.
    public static string[] Normalize(string text)
    {
        var words = text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            if (WordIndex.Value.ContainsKey(words[i]) || words[i].Length < 4) continue;
            var prefix = words[i][..4];
            var matches = WordList.Value.Where(w => w.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 1) words[i] = matches[0];
        }
        return words;
    }

    // Null if the words are a valid BIP39 phrase, otherwise what is wrong with them.
    public static string? Check(string[] words)
    {
        if (words.Length is not (12 or 15 or 18 or 21 or 24))
            return $"Enter 12 recovery words (or 15, 18, 21 or 24). You entered {words.Length}.";
        var unknown = words.Where(w => !WordIndex.Value.ContainsKey(w)).ToArray();
        if (unknown.Length > 0)
            return "Not on the recovery word list: " + string.Join(", ", unknown.Distinct());

        // 11 bits per word: the entropy, then a checksum of length entropy/32
        // bits taken from the start of SHA-256(entropy).
        var bits = new bool[words.Length * 11];
        for (var i = 0; i < words.Length; i++)
        {
            var index = WordIndex.Value[words[i]];
            for (var b = 0; b < 11; b++) bits[i * 11 + b] = (index >> (10 - b) & 1) == 1;
        }
        var checksumBits = bits.Length / 33;
        var entropy = new byte[(bits.Length - checksumBits) / 8];
        for (var i = 0; i < entropy.Length * 8; i++)
            if (bits[i]) entropy[i / 8] |= (byte)(0x80 >> (i % 8));
        var hash = SHA256.HashData(entropy);
        for (var i = 0; i < checksumBits; i++)
        {
            var expected = (hash[i / 8] & (0x80 >> (i % 8))) != 0;
            if (bits[entropy.Length * 8 + i] != expected)
                return "These words don't fit together (the phrase's checksum is wrong). Check each word and the order.";
        }
        return null;
    }

    // BIP39 seed (no extra BIP39 passphrase, as IronMiner and Core-made phrases have none).
    public static byte[] ToSeed(string[] words)
    {
        var phrase = Encoding.UTF8.GetBytes(string.Join(' ', words).Normalize(NormalizationForm.FormKD));
        var salt = Encoding.UTF8.GetBytes("mnemonic");
        var seed = Rfc2898DeriveBytes.Pbkdf2(phrase, salt, 2048, HashAlgorithmName.SHA512, 64);
        CryptographicOperations.ZeroMemory(phrase);
        return seed;
    }

    // BIP32 master extended private key, in TrueNorth's xprv format.
    public static string MasterXprv(byte[] seed, bool mainnet)
    {
        var i = HMACSHA512.HashData(Encoding.ASCII.GetBytes("Bitcoin seed"), seed);
        try
        {
            var key = new BigInteger(i.AsSpan(0, 32), isUnsigned: true, isBigEndian: true);
            if (key.IsZero || key >= CurveOrder)
                throw new Exception("These words give an unusable key (astronomically rare). Use a different phrase.");

            var data = new byte[78];
            var version = mainnet ? MainXprvVersion : TestXprvVersion;
            data[0] = (byte)(version >> 24); data[1] = (byte)(version >> 16);
            data[2] = (byte)(version >> 8); data[3] = (byte)version;
            // depth 0, parent fingerprint 0, child number 0: bytes 4..12 stay zero
            Array.Copy(i, 32, data, 13, 32);   // chain code
            data[45] = 0;
            Array.Copy(i, 0, data, 46, 32);    // private key
            var xprv = Base58Check(data);
            CryptographicOperations.ZeroMemory(data);
            return xprv;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(i);
        }
    }

    // The receiving and change descriptors, each with Core's checksum.
    public static (string Receive, string Change) Descriptors(string xprv, bool mainnet)
    {
        var coin = mainnet ? MainCoinType : TestCoinType;
        var receive = $"qrh({xprv}/88h/{coin}h/0h/0/*)";
        var change = $"qrh({xprv}/88h/{coin}h/0h/1/*)";
        return (receive + "#" + DescriptorChecksum(receive), change + "#" + DescriptorChecksum(change));
    }

    // Creates an encrypted wallet holding the keys of these words, and returns its
    // first receiving address (from Core, so it is exactly what Core will use).
    // clientFor(null) talks to the node, clientFor(name) to the named wallet.
    public static async Task<string> RestoreAsync(Func<string?, RpcClient> clientFor, string walletName,
        string[] words, string passphrase, Action<string> progress)
    {
        var chain = (await clientFor(null).CallAsync("getblockchaininfo")).GetProperty("chain").GetString();
        var mainnet = chain == "main";

        foreach (var w in (await clientFor(null).CallAsync("listwalletdir")).GetProperty("wallets").EnumerateArray())
        {
            if (w.GetProperty("name").GetString() == walletName)
                throw new Exception($"A wallet named '{walletName}' already exists on this node. Choose another name.");
        }

        var seed = ToSeed(words);
        string xprv;
        try { xprv = MasterXprv(seed, mainnet); }
        finally { CryptographicOperations.ZeroMemory(seed); }
        var (receive, change) = Descriptors(xprv, mainnet);

        progress("Creating the wallet...");
        // name, disable_private_keys, blank, passphrase, avoid_reuse, descriptors, load_on_startup
        await clientFor(null).CallAsync("createwallet", walletName, false, true, passphrase, false, true, true);

        var wallet = clientFor(walletName);
        await wallet.CallAsync("walletpassphrase", passphrase, 300);
        try
        {
            progress("Importing the keys and scanning the chain for your coins (this can take several minutes)...");
            // Mainnet: scan from launch. Test networks have no fixed start, so scan them all.
            object timestamp = mainnet ? MainnetLaunch : 0;
            var requests = new object[]
            {
                new Dictionary<string, object> { ["desc"] = receive, ["timestamp"] = timestamp, ["active"] = true, ["internal"] = false },
                new Dictionary<string, object> { ["desc"] = change, ["timestamp"] = timestamp, ["active"] = true, ["internal"] = true },
            };
            JsonElement results;
            try
            {
                results = await wallet.CallAsync("importdescriptors", (object)requests);
            }
            catch (TaskCanceledException)
            {
                // The request timed out on this side; the node keeps importing and scanning.
                throw new Exception($"The node is still scanning the chain for the wallet's coins. Leave it running; "
                    + $"in a few minutes set the wallet name in Settings to {walletName} and press Refresh.");
            }
            var problems = new List<string>();
            foreach (var r in results.EnumerateArray())
            {
                if (!r.GetProperty("success").GetBoolean())
                    problems.Add(r.GetProperty("error").GetProperty("message").GetString() ?? "unknown error");
            }
            if (problems.Count > 0)
                throw new Exception("The wallet was created but its keys could not be imported: " + string.Join("; ", problems));
        }
        finally
        {
            try { await wallet.CallAsync("walletlock"); } catch { }
        }

        var first = await clientFor(null).CallAsync("deriveaddresses", receive, new[] { 0, 0 });
        return first[0].GetString() ?? "";
    }

    private static string Base58Check(byte[] payload)
    {
        const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        var check = SHA256.HashData(SHA256.HashData(payload));
        var data = new byte[payload.Length + 4];
        Array.Copy(payload, data, payload.Length);
        Array.Copy(check, 0, data, payload.Length, 4);

        var value = new BigInteger(data, isUnsigned: true, isBigEndian: true);
        var sb = new StringBuilder();
        while (value > 0)
        {
            value = BigInteger.DivRem(value, 58, out var rem);
            sb.Insert(0, alphabet[(int)rem]);
        }
        foreach (var b in data)
        {
            if (b != 0) break;
            sb.Insert(0, '1');
        }
        CryptographicOperations.ZeroMemory(data);
        return sb.ToString();
    }

    // Bitcoin Core's descriptor checksum (src/script/descriptor.cpp), required by importdescriptors.
    public static string DescriptorChecksum(string descriptor)
    {
        const string inputCharset = "0123456789()[],'/*abcdefgh@:$%{}IJKLMNOPQRSTUVWXYZ&+-.;<=>?!^_|~ijklmnopqrstuvwxyzABCDEFGH`#\"\\ ";
        const string checksumCharset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
        ulong[] generator = { 0xf5dee51989, 0xa9fdca3312, 0x1bab10e32d, 0x3706b1677a, 0x644d626ffd };

        ulong Polymod(ulong c, int val)
        {
            var c0 = c >> 35;
            c = ((c & 0x7ffffffff) << 5) ^ (ulong)val;
            for (var i = 0; i < 5; i++)
                if ((c0 >> i & 1) != 0) c ^= generator[i];
            return c;
        }

        ulong checksum = 1;
        int cls = 0, clsCount = 0;
        foreach (var ch in descriptor)
        {
            var pos = inputCharset.IndexOf(ch);
            if (pos < 0) throw new ArgumentException("Character not allowed in a descriptor: " + ch);
            checksum = Polymod(checksum, pos & 31);
            cls = cls * 3 + (pos >> 5);
            if (++clsCount == 3)
            {
                checksum = Polymod(checksum, cls);
                cls = 0;
                clsCount = 0;
            }
        }
        if (clsCount > 0) checksum = Polymod(checksum, cls);
        for (var j = 0; j < 8; j++) checksum = Polymod(checksum, 0);
        checksum ^= 1;

        var result = new char[8];
        for (var j = 0; j < 8; j++) result[j] = checksumCharset[(int)(checksum >> (5 * (7 - j)) & 31)];
        return new string(result);
    }
}
