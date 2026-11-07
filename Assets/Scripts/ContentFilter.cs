using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// Keeps adult / suggestive models out of the app (search results and recommendations).
///  1. Sketchfab's own flag: any model with isAgeRestricted = true is hidden.
///  2. Keyword check on the model's name and tags (whole words, case-insensitive),
///     because fan-made models are not always flagged by their authors.
/// Used for every list of models the app shows. Add words to <see cref="BlockedWords"/> if needed.
/// </summary>
public static class ContentFilter
{
    private static readonly HashSet<string> BlockedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "nsfw", "nude", "nudes", "nudity", "naked", "sexy", "sex", "sexual", "porn", "porno", "erotic", "erotica",
        "hentai", "ecchi", "lewd", "fanservice", "r18", "18plus", "xxx", "adult", "fetish", "bdsm", "bondage",
        "bikini", "swimsuit", "swimwear", "lingerie", "underwear", "panties", "panty", "bra", "thong", "topless",
        "breast", "breasts", "boob", "boobs", "tits", "busty", "oppai", "nipple", "nipples", "thicc", "booty",
        "butt", "ass", "seductive", "pinup", "pin-up", "stripper", "playboy", "futa", "futanari", "ahegao",
        "waifu", "bunnygirl", "bunnysuit", "succubus"
    };

    private static readonly string[] BlockedPhrases = { "18+", "r-18", "pin up", "bunny girl", "bunny suit" };

    /// <summary>True if the model is OK to show to everyone.</summary>
    public static bool IsSafe(JToken model)
    {
        if (model == null) return false;
        if (model.Value<bool?>("isAgeRestricted") == true) return false;

        if (ContainsBlocked(model.Value<string>("name"))) return false;

        if (model["tags"] is JArray tags)
        {
            foreach (var tag in tags)
            {
                string t = tag.Type == JTokenType.Object ? (tag.Value<string>("name") ?? tag.Value<string>("slug")) : tag.ToString();
                if (ContainsBlocked(t)) return false;
            }
        }
        return true;
    }

    private static bool ContainsBlocked(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        string lower = text.ToLowerInvariant();
        foreach (var phrase in BlockedPhrases)
            if (lower.Contains(phrase)) return true;

        // Whole-word match so "class" or "bass" never trip "ass"
        int start = -1;
        for (int i = 0; i <= lower.Length; i++)
        {
            bool letter = i < lower.Length && (char.IsLetterOrDigit(lower[i]) || lower[i] == '-');
            if (letter && start < 0) start = i;
            else if (!letter && start >= 0)
            {
                if (BlockedWords.Contains(lower.Substring(start, i - start))) return true;
                start = -1;
            }
        }
        return false;
    }
}
