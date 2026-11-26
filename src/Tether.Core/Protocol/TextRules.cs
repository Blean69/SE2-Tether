// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Globalization;
using System.Text;

namespace Tether.Protocol;

/// <summary>
/// Cleans user-supplied text, such as player names and chat, before anyone displays it.
/// </summary>
/// <remarks>
/// Strips control characters and the invisible format characters that can be used to spoof text.
/// Those include bidi overrides and zero-width joiners. It then trims the text and caps its length,
/// without splitting surrogate pairs.
/// </remarks>
public static class TextRules
{
    public const string FallbackPlayerName = "Player";

    public static string SanitizePlayerName(string? name)
    {
        var cleaned = Clean(name, ProtocolConstants.MaxPlayerNameChars);
        return cleaned.Length == 0 ? FallbackPlayerName : cleaned;
    }

    public static string SanitizeSessionName(string? name, string fallback)
    {
        var cleaned = Clean(name, ProtocolConstants.MaxSessionNameChars);
        return cleaned.Length == 0 ? fallback : cleaned;
    }

    /// <summary>Returns an empty string if nothing printable is left; callers drop such messages.</summary>
    public static string SanitizeChat(string? text) => Clean(text, ProtocolConstants.MaxChatChars);

    /// <summary>Cleans text for a disconnect message and truncates it to fit the wire limit.</summary>
    public static string SanitizeDisconnectMessage(string? text)
    {
        var cleaned = Clean(text, ProtocolConstants.MaxDisconnectMessageBytes);
        while (Encoding.UTF8.GetByteCount(cleaned) > ProtocolConstants.MaxDisconnectMessageBytes)
        {
            cleaned = TruncateChars(cleaned, cleaned.Length - 1);
        }

        return cleaned;
    }

    private static string Clean(string? input, int maxChars)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(input.Length, maxChars));
        var lastWasSpace = false;
        foreach (var rune in input.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
            {
                // Tabs and newlines become a single space; everything else in these categories is dropped.
                if (rune.Value is '\t' or '\n' or '\r')
                {
                    AppendSpace(builder, ref lastWasSpace);
                }

                continue;
            }

            if (Rune.IsWhiteSpace(rune))
            {
                AppendSpace(builder, ref lastWasSpace);
                continue;
            }

            builder.Append(rune.ToString());
            lastWasSpace = false;
        }

        var result = builder.ToString().Trim();
        return result.Length <= maxChars ? result : TruncateChars(result, maxChars).TrimEnd();
    }

    private static void AppendSpace(StringBuilder builder, ref bool lastWasSpace)
    {
        if (!lastWasSpace)
        {
            builder.Append(' ');
            lastWasSpace = true;
        }
    }

    private static string TruncateChars(string value, int maxChars)
    {
        if (value.Length <= maxChars)
        {
            return value;
        }

        // Don't cut a surrogate pair in half.
        if (maxChars > 0 && char.IsHighSurrogate(value[maxChars - 1]))
        {
            maxChars--;
        }

        return value.Substring(0, maxChars);
    }
}
