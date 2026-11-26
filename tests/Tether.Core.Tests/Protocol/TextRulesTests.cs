// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System.Text;
using Tether.Protocol;
using Xunit;

namespace Tether.Tests.Protocol;

public class TextRulesTests
{
    [Theory]
    [InlineData("  Ripley  ", "Ripley")]
    [InlineData("Rip\u0000ley", "Ripley")]
    [InlineData("Evil‮esrever", "Evilesrever")] // right-to-left override stripped
    [InlineData("Zero​width", "Zerowidth")]
    [InlineData("tab\tand\nnewline", "tab and newline")]
    [InlineData("many     spaces", "many spaces")]
    [InlineData("", TextRules.FallbackPlayerName)]
    [InlineData("\u0001\u0002", TextRules.FallbackPlayerName)]
    [InlineData(null, TextRules.FallbackPlayerName)]
    public void PlayerName_IsCleaned(string? input, string expected) =>
        Assert.Equal(expected, TextRules.SanitizePlayerName(input));

    [Fact]
    public void PlayerName_IsCappedWithoutSplittingSurrogates()
    {
        var name = new string('a', ProtocolConstants.MaxPlayerNameChars - 1) + "🚀🚀";
        var cleaned = TextRules.SanitizePlayerName(name);
        Assert.True(cleaned.Length <= ProtocolConstants.MaxPlayerNameChars);
        Assert.False(char.IsHighSurrogate(cleaned[^1]));
    }

    [Fact]
    public void Chat_OfOnlyInvisibleCharacters_BecomesEmpty() =>
        Assert.Equal(string.Empty, TextRules.SanitizeChat("​‎\u0007"));

    [Fact]
    public void DisconnectMessage_FitsTheWireLimitInBytes()
    {
        var cleaned = TextRules.SanitizeDisconnectMessage(new string('é', 500));
        Assert.True(Encoding.UTF8.GetByteCount(cleaned) <= ProtocolConstants.MaxDisconnectMessageBytes);
    }
}
