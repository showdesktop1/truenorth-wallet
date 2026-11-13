using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using QRCoder;

namespace TrueNorthWallet;

public partial class MainWindow : Window
{
    private const decimal MiettesPerNorth = 100000000m;
    private bool _testNetwork;
    private string UnitName => _testNetwork ? "tNORTH" : "NORTH";

    private readonly WalletSettings _settings = WalletSettings.Load();
    private RpcClient _rpc;

    public MainWindow()
    {
        InitializeComponent();
        _rpc = CreateClient();

        HostBox.Text = _settings.Host;
        PortBox.Text = _settings.Port.ToString();
        NetworkBox.SelectedIndex = _settings.Port switch { 9554 => 0, 49554 => 1, 18443 => 2, _ => -1 };
        UserBox.Text = _settings.User;
        RpcPasswordBox.Text = _settings.Password;
        WalletBox.Text = _settings.Wallet;
        CookieBox.Text = _settings.CookiePath;
        RememberPassBox.IsChecked = _settings.RememberPassword;
        UnitBox.SelectedIndex = UseMie ? 1 : 0;
        UpdateUnitLabels();

        Opened += (_, _) => HandleStartupLink();
        SingleInstance.StartServer(message => Avalonia.Threading.Dispatcher.UIThread.Post(() => HandleForwardedMessage(message)));
        Opened += async (_, _) =>
        {
            await RefreshAsync();
            await NewAddressAsync();
        };
    }

    private bool UseMie => _settings.Unit == "miettes";

        private void ShowBalance(decimal north)
    {
        BalanceText.Text = "";
        var big = double.IsNaN(BalanceText.FontSize) ? 46.0 : BalanceText.FontSize;
        var small = big / 2;
        var muted = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#53697A"));

        var inlines = BalanceText.Inlines;
        if (inlines == null)
        {
            inlines = new Avalonia.Controls.Documents.InlineCollection();
            BalanceText.Inlines = inlines;
        }
        inlines.Clear();

        if (UseMie)
        {
            inlines.Add(new Avalonia.Controls.Documents.Run(decimal.Round(north * MiettesPerNorth, 0).ToString("N0", CultureInfo.InvariantCulture)));
            inlines.Add(new Avalonia.Controls.Documents.Run(decimal.Round(north * MiettesPerNorth, 0) == 1 ? " miette" : " miettes") { FontSize = small, Foreground = muted });
        }
        else
        {
            var text = north.ToString("N8", CultureInfo.InvariantCulture);
            var dot = text.IndexOf('.');
            inlines.Add(new Avalonia.Controls.Documents.Run(text.Substring(0, dot + 5)));
            inlines.Add(new Avalonia.Controls.Documents.Run(text.Substring(dot + 5)) { FontSize = small });
            inlines.Add(new Avalonia.Controls.Documents.Run(" " + UnitName) { FontSize = small, Foreground = muted });
        }
    }

    private void ClearBalance()
    {
        BalanceText.Inlines?.Clear();
        BalanceText.Text = "-";
    }

    private string FormatAmount(decimal north)
    {
        if (UseMie)
            {
            var miettes = decimal.Round(north * MiettesPerNorth, 0);
            return miettes.ToString("N0", CultureInfo.InvariantCulture) + (Math.Abs(miettes) == 1 ? " miette" : " miettes");
        }
        return north.ToString("N8", CultureInfo.InvariantCulture) + " " + UnitName;
    }

    private void UpdateUnitLabels()
    {
        AmountLabel.Text = UseMie ? "Amount (miettes, whole numbers)" : "Amount (" + UnitName + ")";
    }

    private RpcClient CreateClient()
    {
        var wallet = string.IsNullOrWhiteSpace(_settings.Wallet) ? null : _settings.Wallet.Trim();
        return new RpcClient(_settings.Host, _settings.Port, _settings.User, _settings.Password, wallet, string.IsNullOrWhiteSpace(_settings.CookiePath) ? null : _settings.CookiePath.Trim());
    }

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e) => await RefreshAsync();
    private async void NewAddressButton_Click(object? sender, RoutedEventArgs e) => await NewAddressAsync();

    private void NetworkBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        PortBox.Text = NetworkBox.SelectedIndex switch
        {
            0 => "9554",
            1 => "49554",
            2 => "18443",
            _ => PortBox.Text
        };
    }

    private async void SaveSettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text?.Trim(), out var port) || port < 1 || port > 65535)
        {
            SettingsStatus.Text = "The port must be a number between 1 and 65535.";
            return;
        }

        _settings.Host = string.IsNullOrWhiteSpace(HostBox.Text) ? "127.0.0.1" : HostBox.Text.Trim();
        _settings.Port = port;
        _settings.User = UserBox.Text?.Trim() ?? "";
        _settings.Password = RpcPasswordBox.Text ?? "";
        _settings.Wallet = WalletBox.Text?.Trim() ?? "";
        _settings.CookiePath = CookieBox.Text?.Trim() ?? "";
        _settings.RememberPassword = RememberPassBox.IsChecked == true;
        if (_settings.CookiePath.Length > 0)
        {
            _settings.User = "";
            _settings.Password = "";
            UserBox.Text = "";
            RpcPasswordBox.Text = "";
        }
        _settings.Unit = UnitBox.SelectedIndex == 1 ? "miettes" : "NORTH";
        UpdateUnitLabels();

        try
        {
            _settings.Save();
            _rpc = CreateClient();
            SettingsStatus.Text = "Saved. Testing connection...";
            var chain = await _rpc.CallAsync("getblockchaininfo");
            UpdateNetworkBadge(chain.GetProperty("chain").GetString());
            await _rpc.CallAsync("getwalletinfo");
            SettingsStatus.Text = "Saved. Connected to " + chain.GetProperty("chain").GetString() + ".";
            if (!IsLocalHost(_settings.Host)) SettingsStatus.Text += RemoteWarning;
            await RefreshAsync();
            await NewAddressAsync();
        }
        catch (Exception ex)
        {
            SettingsStatus.Text = "Saved, but the connection failed: " + ex.Message;
            if (!IsLocalHost(_settings.Host)) SettingsStatus.Text += RemoteWarning;
            await RefreshAsync();
        }
    }

        private async Task UpdateWalletStateAsync()
    {
        try
        {
            var info = await _rpc.CallAsync("getwalletinfo");
            if (info.TryGetProperty("unlocked_until", out var until))
                WalletStateText.Text = until.GetInt64() == 0
                    ? "Wallet is encrypted and locked."
                    : "Wallet is encrypted and temporarily unlocked.";
            else
                WalletStateText.Text = "Wallet is NOT encrypted. Encrypt it before holding real coins.";
        }
        catch (Exception ex)
        {
            WalletStateText.Text = "Wallet state unknown: " + ex.Message;
        }
    }

    private async Task<bool> IsEncryptedAsync()
    {
        var info = await _rpc.CallAsync("getwalletinfo");
        return info.TryGetProperty("unlocked_until", out _);
    }

    private bool CheckNewPassphrase(out string pass)
    {
        pass = NewPassBox.Text ?? "";
        if (pass.Length < 8)
        {
            SecurityStatus.Text = "The passphrase must be at least 8 characters.";
            return false;
        }
        if (pass != (NewPassBox2.Text ?? ""))
        {
            SecurityStatus.Text = "The two passphrases do not match.";
            return false;
        }
        if (UnderstandBox.IsChecked != true)
        {
            SecurityStatus.Text = "Tick the box to confirm you understand the passphrase cannot be recovered.";
            return false;
        }
        return true;
    }

    private void ClearPassphraseBoxes()
    {
        NewPassBox.Text = "";
        NewPassBox2.Text = "";
        OldPassBox.Text = "";
        UnderstandBox.IsChecked = false;
    }

    private async void EncryptButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!CheckNewPassphrase(out var pass)) return;
        try
        {
            if (await IsEncryptedAsync())
            {
                SecurityStatus.Text = "This wallet is already encrypted. Use Change passphrase instead.";
                return;
            }
            SecurityStatus.Text = "Encrypting...";
            var result = await _rpc.CallAsync("encryptwallet", pass);
            ClearPassphraseBoxes();
            var message = result.ValueKind == JsonValueKind.String ? result.GetString() : "";
            SecurityStatus.Text = "Wallet encrypted. " + message + "\nIf the node shut down, start it again and press Refresh.";
        }
        catch (Exception ex)
        {
            SecurityStatus.Text = "Error: " + ex.Message;
        }
        await UpdateWalletStateAsync();
    }

    private async void ChangePassButton_Click(object? sender, RoutedEventArgs e)
    {
        var old = OldPassBox.Text ?? "";
        if (old.Length == 0)
        {
            SecurityStatus.Text = "Enter your current passphrase.";
            return;
        }
        if (!CheckNewPassphrase(out var pass)) return;
        try
        {
            await _rpc.CallAsync("walletpassphrasechange", old, pass);
            ClearPassphraseBoxes();
            SecurityStatus.Text = "Passphrase changed.";
        }
        catch (Exception ex)
        {
            SecurityStatus.Text = "Error: " + ex.Message;
        }
    }

    private async void RestoreButton_Click(object? sender, RoutedEventArgs e)
    {
        var name = RestoreNameBox.Text?.Trim() ?? "";
        var pass = RestorePassBox.Text ?? "";
        if (name.Length == 0)
        {
            RestoreStatus.Text = "Enter a name for the restored wallet.";
            return;
        }
        var words = RecoveryWords.Normalize(RestoreWordsBox.Text ?? "");
        var problem = RecoveryWords.Check(words);
        if (problem != null)
        {
            RestoreStatus.Text = problem;
            return;
        }
        if (pass.Length < 8)
        {
            RestoreStatus.Text = "The passphrase must be at least 8 characters.";
            return;
        }
        if (pass != (RestorePassBox2.Text ?? ""))
        {
            RestoreStatus.Text = "The two passphrases do not match.";
            return;
        }
        if (RestoreUnderstandBox.IsChecked != true)
        {
            RestoreStatus.Text = "Tick the box to confirm you understand the passphrase cannot be recovered.";
            return;
        }

        try
        {
            var address = await RecoveryWords.RestoreAsync(CreateClientFor, name, words, pass, msg => RestoreStatus.Text = msg);
            RestoreWordsBox.Text = "";
            RestorePassBox.Text = "";
            RestorePassBox2.Text = "";
            RestoreUnderstandBox.IsChecked = false;
            RestoreStatus.Text = $"Wallet '{name}' restored. Its first address is\n{address}\n"
                + "Check that this is the address you had. To use the wallet, set the wallet name in Settings to " + name + ".";
        }
        catch (Exception ex)
        {
            RestoreStatus.Text = "Error: " + ex.Message;
        }
    }

    private async void LockButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _rpc.CallAsync("walletlock");
            SecurityStatus.Text = "Wallet locked.";
        }
        catch (Exception ex)
        {
            SecurityStatus.Text = "Error: " + ex.Message;
        }
        await UpdateWalletStateAsync();
    }

        private void ApplyPaymentRequest(PaymentRequest req)
    {
        SendAddressBox.Text = req.Address;
        if (req.Amount.HasValue)
            SendAmountBox.Text = UseMie
                ? decimal.Round(req.Amount.Value * MiettesPerNorth, 0).ToString(CultureInfo.InvariantCulture)
                : req.Amount.Value.ToString("0.########", CultureInfo.InvariantCulture);
        else
            SendAmountBox.Text = "";

        var info = new System.Collections.Generic.List<string>();
        if (req.Label.Length > 0) info.Add("Label: " + req.Label);
        if (req.Message.Length > 0) info.Add("Message: " + req.Message);
        LinkInfoText.Text = string.Join("\n", info);

        SendStatus.Text = "Payment details loaded from the link. Check the address and amount, then press Send.";
    }

    private void HandleStartupLink()
    {
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (!arg.StartsWith("truenorth:", StringComparison.OrdinalIgnoreCase)) continue;

            if (PaymentRequest.TryParse(arg, out var req, out var error))
            {
                LinkBox.Text = arg;
                ApplyPaymentRequest(req);
            }
            else
            {
                SendStatus.Text = error;
            }
            Tabs.SelectedIndex = 2;
            break;
        }
    }

    private void LoadLinkButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!PaymentRequest.TryParse(LinkBox.Text, out var req, out var error))
        {
            SendStatus.Text = error;
            return;
        }
        ApplyPaymentRequest(req);
    }

    private async void CopyLinkButton_Click(object? sender, RoutedEventArgs e)
    {
        var req = new PaymentRequest
        {
            Address = AddressBox.Text?.Trim() ?? "",
            Label = RequestLabelBox.Text?.Trim() ?? ""
        };
        if (req.Address.Length == 0)
        {
            ReceiveStatus.Text = "There is no address yet.";
            return;
        }

        var amountText = RequestAmountBox.Text?.Trim() ?? "";
        if (amountText.Length > 0)
        {
            if (!TryParseAmountText(amountText, out var typed, out var linkAmountError))
            {
                ReceiveStatus.Text = linkAmountError;
                return;
            }
            if (UseMie)
            {
                if (typed != decimal.Truncate(typed))
                {
                    ReceiveStatus.Text = "Amounts in miettes must be whole numbers.";
                    return;
                }
                req.Amount = typed / MiettesPerNorth;
            }
            else
            {
                if (decimal.Round(typed, 8) != typed)
                {
                    ReceiveStatus.Text = "NORTH amounts can have at most 8 decimal places.";
                    return;
                }
                req.Amount = typed;
            }
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            var uri = req.ToUri();
            await clipboard.SetTextAsync(uri);
            ReceiveStatus.Text = "Payment link copied:\n" + uri;
        }
    }

        private readonly System.Collections.Generic.List<string> _bumpTxids = new();

    private async Task LoadBumpableAsync()
    {
        var txs = await _rpc.CallAsync("listtransactions", "*", 50);
        var seen = new System.Collections.Generic.HashSet<string>();
        var items = new System.Collections.Generic.List<string>();
        _bumpTxids.Clear();

        foreach (var tx in txs.EnumerateArray())
        {
            if (tx.GetProperty("category").GetString() != "send") continue;
            if (tx.GetProperty("confirmations").GetInt32() != 0) continue;
            if (!tx.TryGetProperty("bip125-replaceable", out var r) || r.GetString() != "yes") continue;
            if (tx.TryGetProperty("abandoned", out var ab) && ab.ValueKind == System.Text.Json.JsonValueKind.True) continue;

            var txid = tx.GetProperty("txid").GetString() ?? "";
            if (txid.Length < 8 || !seen.Add(txid)) continue;

            var amount = Math.Abs(tx.GetProperty("amount").GetDecimal());
            var address = tx.TryGetProperty("address", out var a) ? a.GetString() : "";
            items.Add($"{FormatAmount(amount)} to {address}  ({txid.Substring(0, 8)}...)");
            _bumpTxids.Add(txid);
        }

        BumpBox.ItemsSource = items;
        BumpBox.SelectedIndex = items.Count > 0 ? 0 : -1;
        BumpStatus.Text = items.Count == 0 ? "No unconfirmed payments that can be sped up." : "";
    }

    private async void BumpButton_Click(object? sender, RoutedEventArgs e)
    {
        var index = BumpBox.SelectedIndex;
        if (index < 0 || index >= _bumpTxids.Count)
        {
            BumpStatus.Text = "Choose an unconfirmed payment first.";
            return;
        }

        var txid = _bumpTxids[index];
        var rateText = BumpRateBox.Text?.Trim() ?? "";
        var options = new Dictionary<string, object>();
        if (rateText.Length > 0)
        {
            if (!TryParseAmountText(rateText, out var rate, out _))
            {
                BumpStatus.Text = "Enter the fee rate as a number greater than zero. Use only digits and a dot.";
                return;
            }
            options["fee_rate"] = rate;
        }

        var passphrase = BumpPassBox.Text ?? "";
        var unlocked = false;
        try
        {
            if (passphrase.Length == 0 && await IsEncryptedAsync())
            {
                BumpStatus.Text = "This wallet is encrypted. Enter your wallet passphrase.";
                return;
            }

            BumpStatus.Text = "Working out the new fee...";

            decimal? oldRate = null;
            try
            {
                var entry = await _rpc.CallAsync("getmempoolentry", txid);
                var vsize = entry.GetProperty("vsize").GetDecimal();
                var baseFee = entry.GetProperty("fees").GetProperty("base").GetDecimal();
                oldRate = decimal.Round(baseFee * MiettesPerNorth / vsize, 2);
            }
            catch
            {
            }

            var autoNote = "";
            if (rateText.Length == 0)
            {
                if (oldRate.HasValue)
                {
                    var newRate = oldRate.Value + 1;
                    options["fee_rate"] = newRate;
                    autoNote = $"New fee rate: {newRate.ToString("0.##", CultureInfo.InvariantCulture)} miettes per byte (current {oldRate.Value.ToString("0.##", CultureInfo.InvariantCulture)} plus 1).";
                }
                else
                {
                    autoNote = "The wallet chose the new fee rate automatically.";
                }
            }
            else if (oldRate.HasValue && options.TryGetValue("fee_rate", out var typedRate) && (decimal)typedRate <= oldRate.Value)
            {
                BumpStatus.Text = $"The new rate must be higher than the current rate of {oldRate.Value.ToString("0.##", CultureInfo.InvariantCulture)} miettes per byte.";
                return;
            }
            var preview = await _rpc.CallAsync("psbtbumpfee", txid, options);

            if (preview.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0)
            {
                var reasons = new List<string>();
                foreach (var item in errs.EnumerateArray()) reasons.Add(item.GetString() ?? "");
                BumpStatus.Text = "The fee cannot be raised: " + string.Join("; ", reasons);
                return;
            }

            var oldFee = preview.GetProperty("origfee").GetDecimal();
            var newFee = preview.GetProperty("fee").GetDecimal();
            var summary = BumpBox.SelectedItem?.ToString() ?? txid;
            var rateNote = autoNote.Length > 0 ? autoNote : "The new fee rate is the one you entered.";

            var head = "Replace this unconfirmed payment with a higher-fee version?\n\n" + summary;
            var tail = $"Current fee: {FormatAmount(oldFee)}\nNew fee: {FormatAmount(newFee)}\nExtra cost: {FormatAmount(newFee - oldFee)}\n{rateNote}\n\nThe old version is cancelled and the new one takes its place.";
            if (!await ConfirmAsync("Confirm fee increase", head, null, tail, "Raise fee"))
            {
                BumpStatus.Text = "Cancelled. The fee was not changed.";
                return;
            }

            if (passphrase.Length > 0)
            {
                await _rpc.CallAsync("walletpassphrase", passphrase, 30);
                unlocked = true;
            }

            BumpStatus.Text = "Raising the fee...";
            var result = await _rpc.CallAsync("bumpfee", txid, options);
            var newTxid = result.GetProperty("txid").GetString();
            var before = result.GetProperty("origfee").GetDecimal();
            var after = result.GetProperty("fee").GetDecimal();

            BumpPassBox.Text = "";
            BumpRateBox.Text = "";
            await RefreshAsync();
            BumpStatus.Text = $"Fee raised from {FormatAmount(before)} to {FormatAmount(after)}.\nNew transaction ID:\n{newTxid}";
        }
        catch (Exception ex)
        {
            BumpStatus.Text = "Error: " + ex.Message;
        }
        finally
        {
            if (unlocked)
            {
                try { await _rpc.CallAsync("walletlock"); } catch { }
            }
        }
    }

    private async Task CopyTextAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null && text.Length > 0) await clipboard.SetTextAsync(text);
    }

    private async void CopyExportButton_Click(object? sender, RoutedEventArgs e) => await CopyTextAsync(ExportBox.Text ?? "");
    private async void CopyPsbtButton_Click(object? sender, RoutedEventArgs e) => await CopyTextAsync(PsbtBox.Text ?? "");

    private bool TryParseAmountNorth(string? text, out decimal north, out string error)
    {
        north = 0;
        error = "";
        if (!TryParseAmountText(text, out var typed, out var parseError))
        {
            error = parseError;
            return false;
        }
        if (UseMie)
        {
            if (typed != decimal.Truncate(typed))
            {
                error = "Amounts in miettes must be whole numbers.";
                return false;
            }
            north = typed / MiettesPerNorth;
        }
        else
        {
            if (decimal.Round(typed, 8) != typed)
            {
                error = "NORTH amounts can have at most 8 decimal places.";
                return false;
            }
            north = typed;
        }
        return true;
    }

    private async void ExportDescriptorsButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _rpc.CallAsync("listdescriptors");
            var list = new List<Dictionary<string, JsonElement>>();
            foreach (var d in result.GetProperty("descriptors").EnumerateArray())
            {
                var entry = new Dictionary<string, JsonElement>();
                foreach (var name in new[] { "desc", "timestamp", "active", "internal", "range", "next_index" })
                {
                    if (d.TryGetProperty(name, out var v)) entry[name] = v;
                }
                list.Add(entry);
            }
            ExportBox.Text = JsonSerializer.Serialize(list);
            ExportStatus.Text = $"{list.Count} descriptors exported. They hold public keys only, never private keys.";
        }
        catch (Exception ex)
        {
            ExportStatus.Text = "Error: " + ex.Message;
        }
    }

    private async void CreateWatchButton_Click(object? sender, RoutedEventArgs e)
    {
        var name = WatchNameBox.Text?.Trim() ?? "";
        var json = ImportBox.Text?.Trim() ?? "";
        if (name.Length == 0)
        {
            WatchStatus.Text = "Enter a name for the new wallet.";
            return;
        }

        JsonElement requests;
        try
        {
            requests = JsonSerializer.Deserialize<JsonElement>(json);
            if (requests.ValueKind != JsonValueKind.Array) throw new Exception("not a list");
        }
        catch
        {
            WatchStatus.Text = "The pasted text is not valid descriptor data. Use Export in the wallet that holds the keys.";
            return;
        }

        try
        {
            WatchStatus.Text = "Creating wallet...";
            await CreateClientFor(null).CallAsync("createwallet", name, true, true);

            WatchStatus.Text = "Importing descriptors (this may take a moment)...";
            var results = await CreateClientFor(name).CallAsync("importdescriptors", requests);

            var ok = 0;
            var problems = new List<string>();
            foreach (var r in results.EnumerateArray())
            {
                if (r.GetProperty("success").GetBoolean()) ok++;
                else problems.Add(r.GetProperty("error").GetProperty("message").GetString() ?? "unknown error");
            }

            WatchStatus.Text = $"Wallet '{name}' created. {ok} descriptor(s) imported"
                + (problems.Count > 0 ? $", {problems.Count} failed:\n" + string.Join("\n", problems) : ".")
                + $"\nTo use it, set the wallet name in Settings to {name}.";
        }
        catch (Exception ex)
        {
            WatchStatus.Text = "Error: " + ex.Message;
        }
    }

    private async void CreatePsbtButton_Click(object? sender, RoutedEventArgs e)
    {
        var address = PsbtAddressBox.Text?.Trim() ?? "";
        if (address.Length == 0)
        {
            PsbtStatus.Text = "Enter a recipient address.";
            return;
        }
        if (!TryParseAmountNorth(PsbtAmountBox.Text, out var amount, out var error))
        {
            PsbtStatus.Text = error;
            return;
        }

        try
        {
            var check = await _rpc.CallAsync("validateaddress", address);
            if (!check.GetProperty("isvalid").GetBoolean())
            {
                PsbtStatus.Text = "The node says this address is not valid.";
                return;
            }

            var outputs = new[] { new Dictionary<string, decimal> { [address] = amount } };
            var result = await _rpc.CallAsync("walletcreatefundedpsbt", new object[0], outputs, 0, new Dictionary<string, object>(), true);

            PsbtBox.Text = result.GetProperty("psbt").GetString();
            var fee = result.GetProperty("fee").GetDecimal();
            PsbtStatus.Text = $"Unsigned payment created. Fee: {FormatAmount(fee)}.\nPress Review to check it, then sign it with the wallet that holds the keys.";
        }
        catch (Exception ex)
        {
            PsbtStatus.Text = "Error: " + ex.Message;
        }
    }

    private async void InspectPsbtButton_Click(object? sender, RoutedEventArgs e)
    {
        var psbt = PsbtBox.Text?.Trim() ?? "";
        if (psbt.Length == 0)
        {
            PsbtStatus.Text = "Paste or create a payment first.";
            return;
        }

        try
        {
            var d = await _rpc.CallAsync("decodepsbt", psbt);
            var lines = new List<string>();
            foreach (var o in d.GetProperty("tx").GetProperty("vout").EnumerateArray())
            {
                var value = o.GetProperty("value").GetDecimal();
                var spk = o.GetProperty("scriptPubKey");
                var addr = spk.TryGetProperty("address", out var a) ? a.GetString() : "(no address)";
                lines.Add($"  {FormatAmount(value)} to {addr}");
            }
            var fee = d.TryGetProperty("fee", out var f) ? FormatAmount(f.GetDecimal()) : "unknown";

            var analysis = await _rpc.CallAsync("analyzepsbt", psbt);
            var next = analysis.TryGetProperty("next", out var n) ? n.GetString() : "unknown";

            PsbtStatus.Text = "Outputs (one of them may be change back to the wallet):\n" + string.Join("\n", lines)
                + "\nFee: " + fee + "\nNext step needed: " + next;
        }
        catch (Exception ex)
        {
            PsbtStatus.Text = "Error: " + ex.Message;
        }
    }

    private async void SignPsbtButton_Click(object? sender, RoutedEventArgs e)
    {
        var psbt = PsbtBox.Text?.Trim() ?? "";
        if (psbt.Length == 0)
        {
            PsbtStatus.Text = "Paste or create a payment first.";
            return;
        }

        var passphrase = PsbtPassBox.Text ?? "";
        var unlocked = false;
        try
        {
            if (passphrase.Length == 0 && await IsEncryptedAsync())
            {
                PsbtStatus.Text = "This wallet is encrypted. Enter your wallet passphrase.";
                return;
            }
            if (passphrase.Length > 0)
            {
                await _rpc.CallAsync("walletpassphrase", passphrase, 30);
                unlocked = true;
            }

            var result = await _rpc.CallAsync("walletprocesspsbt", psbt, true);
            PsbtBox.Text = result.GetProperty("psbt").GetString();
            var complete = result.GetProperty("complete").GetBoolean();
            PsbtPassBox.Text = "";
            PsbtStatus.Text = complete
                ? "Signed. The payment is complete and ready to broadcast."
                : "The wallet processed the payment but it is not fully signed. This is expected if this wallet has no keys.";
        }
        catch (Exception ex)
        {
            PsbtStatus.Text = "Error: " + ex.Message;
        }
        finally
        {
            if (unlocked)
            {
                try { await _rpc.CallAsync("walletlock"); } catch { }
            }
        }
    }

    private async void BroadcastPsbtButton_Click(object? sender, RoutedEventArgs e)
    {
        var psbt = PsbtBox.Text?.Trim() ?? "";
        if (psbt.Length == 0)
        {
            PsbtStatus.Text = "Paste or create a payment first.";
            return;
        }
        if (PsbtCheckBox.IsChecked != true)
        {
            PsbtStatus.Text = "Press Review, check the recipients and amounts, then tick the box.";
            return;
        }

        try
        {
            var fin = await _rpc.CallAsync("finalizepsbt", psbt, true);
            if (!fin.GetProperty("complete").GetBoolean())
            {
                PsbtStatus.Text = "The payment is not fully signed yet. Sign it with the wallet that holds the keys first.";
                return;
            }

            var hex = fin.GetProperty("hex").GetString() ?? "";
            var txid = (await _rpc.CallAsync("sendrawtransaction", hex)).GetString();
            PsbtCheckBox.IsChecked = false;
            PsbtStatus.Text = "Sent. Transaction ID:\n" + txid;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            PsbtStatus.Text = "Error: " + ex.Message;
        }
    }

        private const string RemoteWarning = "\nWarning: this node is not on this computer and the connection is not encrypted. Anyone on the network path can read your RPC login and wallet activity. Use a network you trust, or an SSH tunnel or VPN.";

    private static bool IsLocalHost(string host)
    {
        host = (host ?? "").Trim().ToLowerInvariant();
        return host == "localhost" || host == "::1" || host.StartsWith("127.");
    }

    private static bool TryParseAmountText(string? text, out decimal value, out string error)
    {
        value = 0;
        error = "";
        text = text?.Trim() ?? "";
        if (text.Contains(',') || text.Contains(' '))
        {
            error = "Use only digits and a dot for decimals. Commas and spaces are not accepted.";
            return false;
        }
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) || value <= 0)
        {
            error = "Enter a valid amount greater than zero.";
            return false;
        }
        return true;
    }

    private async Task<bool> ConfirmAsync(string title, string before, string? address, string after, string confirmLabel)
    {
        var result = false;
        var ink = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#12263A"));
        var dialog = new Window
        {
            Title = title,
            Width = 700,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            FontFamily = FontFamily,
            FontSize = 14,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#EEF3F5"))
        };

        var panel = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 14 };
        panel.Children.Add(new SelectableTextBlock { Text = before, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = ink });
        if (!string.IsNullOrEmpty(address))
        {
            panel.Children.Add(new SelectableTextBlock
            {
                Text = address,
                TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
                FontFamily = new Avalonia.Media.FontFamily("Cascadia Mono, Consolas, DejaVu Sans Mono, Menlo, monospace"),
                FontSize = 13,
                Foreground = ink
            });
        }
        panel.Children.Add(new SelectableTextBlock { Text = after, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = ink });

        var cancel = new Button { Content = "Cancel" };
        var confirm = new Button
        {
            Content = confirmLabel,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1B7F6B")),
            Foreground = Avalonia.Media.Brushes.White
        };
        cancel.Click += (_, _) => dialog.Close();
        confirm.Click += (_, _) => { result = true; dialog.Close(); };

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);

        dialog.Content = panel;
        await dialog.ShowDialog(this);
        return result;
    }
    private RpcClient CreateClientFor(string? wallet)
    {
        return new RpcClient(_settings.Host, _settings.Port, _settings.User, _settings.Password,
            string.IsNullOrWhiteSpace(wallet) ? null : wallet.Trim(),
            string.IsNullOrWhiteSpace(_settings.CookiePath) ? null : _settings.CookiePath.Trim());
    }
        private void HandleForwardedMessage(string message)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;

        if (!message.StartsWith("truenorth:", StringComparison.OrdinalIgnoreCase)) return;

        if (PaymentRequest.TryParse(message, out var req, out var error))
        {
            LinkBox.Text = message;
            ApplyPaymentRequest(req);
        }
        else
        {
            SendStatus.Text = error;
        }
        Tabs.SelectedIndex = 2;
    }

        private void UpdateNetworkBadge(string? chain)
    {
        var name = (chain ?? "").Trim().ToLowerInvariant();
        var label = name switch
        {
            "test" => "TESTNET3",
            "testnet4" => "TESTNET4",
            "regtest" => "REGTEST",
            "signet" => "SIGNET",
            _ => ""
        };
        NetworkBadgeText.Text = label;
        NetworkBadge.IsVisible = label.Length > 0;
        _testNetwork = label.Length > 0;
        UpdateUnitLabels();
    }

    private async void CopyButton_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null && !string.IsNullOrEmpty(AddressBox.Text))
        {
            await clipboard.SetTextAsync(AddressBox.Text);
            ReceiveStatus.Text = "Address copied";
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            StatusText.Text = "Connecting...";
            var chain = await _rpc.CallAsync("getblockchaininfo");
            UpdateNetworkBadge(chain.GetProperty("chain").GetString());
            var balance = await _rpc.CallAsync("getbalance");
            await LoadHistoryAsync();
            await LoadBumpableAsync();
            await UpdateWalletStateAsync();

            ShowBalance(balance.GetDecimal());
            UpdateNetworkBadge(chain.GetProperty("chain").GetString());
            StatusText.Text = $"Connected to {chain.GetProperty("chain").GetString()}, block {chain.GetProperty("blocks").GetInt32()}";
            if (!IsLocalHost(_settings.Host)) StatusText.Text += " - warning: this connection to a remote node is not encrypted.";
        }
        catch (Exception ex)
        {
            ClearBalance();
            HistoryBox.Text = "";
            NetworkBadge.IsVisible = false;
            StatusText.Text = "Error: " + ex.Message;
        }
    }

    private async Task NewAddressAsync()
    {
        try
        {
            ReceiveStatus.Text = "Requesting address...";
            var address = (await _rpc.CallAsync("getnewaddress")).GetString() ?? "";
            AddressBox.Text = address;
            QrImage.Source = MakeQr(address);
            ReceiveStatus.Text = "";
        }
        catch (Exception ex)
        {
            ReceiveStatus.Text = "Error: " + ex.Message;
        }
    }

    private async void SendButton_Click(object? sender, RoutedEventArgs e)
    {
        var address = SendAddressBox.Text?.Trim() ?? "";
        var passphrase = PassphraseBox.Text ?? "";

        if (address.Length == 0)
        {
            SendStatus.Text = "Enter a recipient address.";
            return;
        }
        if (!TryParseAmountText(SendAmountBox.Text, out var typed, out var sendAmountError))
        {
            SendStatus.Text = sendAmountError;
            return;
        }

        decimal amount;
        if (UseMie)
        {
            if (typed != decimal.Truncate(typed))
            {
                SendStatus.Text = "Amounts in miettes must be whole numbers.";
                return;
            }
            amount = typed / MiettesPerNorth;
        }
        else
        {
            if (decimal.Round(typed, 8) != typed)
            {
                SendStatus.Text = "NORTH amounts can have at most 8 decimal places.";
                return;
            }
            amount = typed;
        }

        var unlocked = false;
        try
        {
            SendStatus.Text = "Checking address...";
            var check = await _rpc.CallAsync("validateaddress", address);
            if (!check.GetProperty("isvalid").GetBoolean())
            {
                SendStatus.Text = "The node says this address is not valid.";
                return;
            }
            decimal? previewFee = null;
            var previewNote = "";
            try
            {
                var previewOutputs = new[] { new Dictionary<string, decimal> { [address] = amount } };
                var preview = await _rpc.CallAsync("walletcreatefundedpsbt", new object[0], previewOutputs, 0, new Dictionary<string, object>(), true);
                previewFee = preview.GetProperty("fee").GetDecimal();
            }
            catch (Exception previewError)
            {
                var m = previewError.Message;
                if (m.Contains("nsufficient") || m.Contains("too small") || m.Contains("Invalid amount") || m.Contains("dust"))
                {
                    SendStatus.Text = "Error: " + m;
                    return;
                }
                previewNote = m;
            }

            var confirmHead = $"Send {FormatAmount(amount)} to:";
            var confirmTail = (previewFee.HasValue
                    ? $"Estimated network fee: {FormatAmount(previewFee.Value)}\nTotal leaving your wallet: {FormatAmount(amount + previewFee.Value)}"
                    : "Network fee: could not be estimated" + (previewNote.Length > 0 ? " (" + previewNote + ")" : ""))
                + "\n\nThis cannot be undone.";
            if (!await ConfirmAsync("Confirm payment", confirmHead, address, confirmTail, "Send"))
            {
                SendStatus.Text = "Cancelled. Nothing was sent.";
                return;
            }

            if (passphrase.Length == 0 && await IsEncryptedAsync())
            {
                SendStatus.Text = "This wallet is encrypted. Enter your wallet passphrase to send.";
                return;
            }

            if (passphrase.Length > 0)
            {
                await _rpc.CallAsync("walletpassphrase", passphrase, 30);
                unlocked = true;
            }

            SendStatus.Text = "Sending...";
            var txid = (await _rpc.CallAsync("sendtoaddress", address, amount)).GetString();
            SendStatus.Text = "Sent. Transaction ID:\n" + txid;
            SendAmountBox.Text = "";
            PassphraseBox.Text = "";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            SendStatus.Text = "Error: " + ex.Message;
        }
        finally
        {
            if (unlocked)
            {
                try { await _rpc.CallAsync("walletlock"); } catch { }
            }
        }
    }

    private async Task LoadHistoryAsync()
    {
        var txs = await _rpc.CallAsync("listtransactions", "*", 50);
        var lines = new System.Collections.Generic.List<string>();
        foreach (var tx in txs.EnumerateArray())
        {
            var when = DateTimeOffset.FromUnixTimeSeconds(tx.GetProperty("time").GetInt64()).LocalDateTime;
            var category = tx.GetProperty("category").GetString();
            var amount = tx.GetProperty("amount").GetDecimal();
            var confs = tx.GetProperty("confirmations").GetInt32();
            var address = tx.TryGetProperty("address", out var a) ? a.GetString() : "";
            lines.Add($"{when:yyyy-MM-dd HH:mm}  {category,-9} {FormatAmount(amount),28}  conf {confs,4}\n    {address}");
        }
        lines.Reverse();
        HistoryBox.Text = lines.Count == 0 ? "No transactions yet." : string.Join("\n", lines);
    }

    private static Bitmap MakeQr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10);
        return new Bitmap(new MemoryStream(png));
    }
}


















