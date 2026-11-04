using System;
using System.Collections.Generic;
using System.Globalization;

namespace TrueNorthWallet;

public class PaymentRequest
{
    public string Address { get; set; } = "";
    public decimal? Amount { get; set; }   // in NORTH
    public string Label { get; set; } = "";
    public string Message { get; set; } = "";

    public static bool TryParse(string? text, out PaymentRequest request, out string error)
    {
        request = new PaymentRequest();
        error = "";
        text = text?.Trim() ?? "";

        const string scheme = "truenorth:";
        if (!text.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            error = "This is not a truenorth: link.";
            return false;
        }

        var rest = text.Substring(scheme.Length);
        if (rest.StartsWith("//")) rest = rest.Substring(2);

        var queryStart = rest.IndexOf('?');
        var address = queryStart >= 0 ? rest.Substring(0, queryStart) : rest;
        var query = queryStart >= 0 ? rest.Substring(queryStart + 1) : "";

        address = Uri.UnescapeDataString(address).Trim().TrimEnd('/');
        if (address.Length == 0)
        {
            error = "The link has no address.";
            return false;
        }
        request.Address = address;

        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = (eq >= 0 ? part.Substring(0, eq) : part).ToLowerInvariant();
            var value = eq >= 0 ? Uri.UnescapeDataString(part.Substring(eq + 1)) : "";

            switch (key)
            {
                case "amount":
                    if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
                        || amount <= 0 || decimal.Round(amount, 8) != amount)
                    {
                        error = "The amount in the link is not valid.";
                        return false;
                    }
                    request.Amount = amount;
                    break;
                case "label":
                    request.Label = value;
                    break;
                case "message":
                    request.Message = value;
                    break;
                default:
                    if (key.StartsWith("req-"))
                    {
                        error = "The link needs a feature this wallet does not support: " + key;
                        return false;
                    }
                    break;
            }
        }
        return true;
    }

    public string ToUri()
    {
        var parts = new List<string>();
        if (Amount.HasValue) parts.Add("amount=" + Amount.Value.ToString("0.########", CultureInfo.InvariantCulture));
        if (Label.Length > 0) parts.Add("label=" + Uri.EscapeDataString(Label));
        if (Message.Length > 0) parts.Add("message=" + Uri.EscapeDataString(Message));
        return "truenorth:" + Address + (parts.Count > 0 ? "?" + string.Join("&", parts) : "");
    }
}

