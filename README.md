<p align="center">
  <img src="Assets/logo-leaf.png" alt="TrueNorth" width="120">
</p>

<h1 align="center">TrueNorth Wallet</h1>

<p align="center">A desktop wallet for the TrueNorth coin (NORTH). Windows and Linux.</p>

> **Early release.** The wallet has not been audited and has not been used on mainnet. Try it on a test network first, and do not use it with funds you cannot afford to lose.

## Download

Get the latest version from the [Releases page](https://github.com/showdesktop1/truenorth-wallet/releases).

| Platform | File | How to run |
|---|---|---|
| Windows (64-bit) | `truenorth-wallet-0.1.0-windows-x64.zip` | Unzip, then run `TrueNorthWallet.exe` |
| Linux (64-bit) | `truenorth-wallet-0.1.0-linux-x64.tar.gz` | `tar -xzf` the file, then run `./TrueNorthWallet` |
| macOS | not yet available | |

**Windows:** the program is not code-signed, so Windows SmartScreen may show a warning the first time. Choose **More info**, then **Run anyway**.

**Linux:** install the libraries the wallet needs, then optionally run `./install.sh` to add it to your application menu and open `truenorth:` links.

    sudo apt install -y libicu-dev libfontconfig1 libice6 libsm6 libx11-6 libxcursor1 libxrandr2 libxi6

**Check your download** against the `SHA256SUMS` file on the release page:

    sha256sum -c SHA256SUMS                          # Linux
    Get-FileHash .\truenorth-wallet-0.1.0-windows-x64.zip   # Windows, compare the hash by eye

## Features

- **Send and receive** with QR codes, a confirmation window before every payment, and `truenorth:` payment links
- **History** with fee bumping for unconfirmed payments, either automatic or at a fee rate you choose
- **Security**: wallet encryption, passphrase change and lock, and cookie authentication so no password has to be stored
- **Watch-only wallets and PSBTs**: prepare a payment on one wallet, sign it on another, then broadcast
- **NORTH or miettes** (1 NORTH = 100,000,000 miettes), with amounts checked strictly so a misplaced comma can never change a payment
- **Test networks** are marked on every screen, so test coins are never mistaken for real ones
- **One window**: opening a `truenorth:` link while the wallet is running fills in the Send screen of the open copy

The wallet holds no keys and no blockchain data of its own. It connects to your own TrueNorth node, and the node's wallet keeps the keys.

## Quick start

1. **Run a TrueNorth node** with RPC enabled (`server=1`). See the [node repository](https://github.com/truenorth-project/truenorth) for how to build and run it, and `doc/testnet.md` there for the test networks.
2. **Start the wallet**, then open **Settings**.
3. **Choose the network**, enter your **wallet name**, and set **Cookie file** to the `.cookie` file in your node's data folder. Press **Save and test connection**.

| Network | RPC port |
|---|---|
| Mainnet | 9554 |
| Testnet4 | 49554 |
| Regtest | 18443 |

The cookie file is created by the node while it is running, for example `~/.truenorth-testnet4/testnet4/.cookie`. Windows users running the node inside WSL can use the `\\wsl.localhost\...` path to the same file.

## Build from source

You need the [.NET 9 SDK](https://dotnet.microsoft.com/download).

    dotnet run

To build a single-file program for your platform:

    dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
    dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

On Linux, install the libraries listed above first.

## Platforms

| Platform | Status |
|---|---|
| Windows | Supported |
| Linux | Works; tested under WSL |
| macOS | Not yet available |

## Privacy and security

- Settings are stored per user: `%APPDATA%\TrueNorthWallet\settings.json` on Windows and `~/.config/TrueNorthWallet/settings.json` on Linux. A password is only saved if you choose to, and it is saved unencrypted. Cookie authentication avoids this.
- The connection to the node is plain HTTP, which is fine for a node on the same computer. The wallet warns you if the node is somewhere else.

## License

The source code is released under the license in the [LICENSE](LICENSE) file. The logo and icon are used with permission and are not covered by that license.

