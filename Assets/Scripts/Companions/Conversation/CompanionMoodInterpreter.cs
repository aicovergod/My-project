using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Companions.Conversation
{
    /// <summary>
    /// Describes how intensely the player is experiencing a mood.
    /// </summary>
    public enum CompanionMoodIntensity
    {
        Unknown = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    /// <summary>
    /// High-level polarity of a mood. Used by the conversation service to steer empathy lines.
    /// </summary>
    public enum CompanionMoodValence
    {
        Neutral = 0,
        Positive = 1,
        Negative = 2
    }

    /// <summary>
    /// Immutable container describing the interpreted mood payload extracted from player chat.
    /// </summary>
    [Serializable]
    public readonly struct CompanionMoodInterpretation
    {
        private readonly string descriptor;

        public CompanionMoodInterpretation(
            string descriptor,
            CompanionMoodValence valence,
            CompanionMoodIntensity intensity,
            bool wasNegated,
            bool hasExplicitIntensity)
        {
            this.descriptor = string.IsNullOrWhiteSpace(descriptor) ? string.Empty : descriptor.Trim();
            Valence = valence;
            Intensity = intensity;
            WasNegated = wasNegated;
            HasExplicitIntensity = hasExplicitIntensity;
        }

        /// <summary>Human readable descriptor for the player's mood (e.g. "feeling tired").</summary>
        public string Descriptor => descriptor ?? string.Empty;

        /// <summary>Polarity classification derived from the parsed text.</summary>
        public CompanionMoodValence Valence { get; }

        /// <summary>Relative strength of the mood after applying intensifiers or downtoners.</summary>
        public CompanionMoodIntensity Intensity { get; }

        /// <summary>True when a negation word ("not", "never") modified the detected mood.</summary>
        public bool WasNegated { get; }

        /// <summary>True when an explicit intensity modifier ("really", "super") influenced the result.</summary>
        public bool HasExplicitIntensity { get; }

        /// <summary>Convenience flag indicating whether a mood descriptor was actually resolved.</summary>
        public bool HasMood => !string.IsNullOrWhiteSpace(descriptor);

        /// <summary>Returns an empty interpretation used when no mood could be derived.</summary>
        public static CompanionMoodInterpretation Empty => default;
    }

    /// <summary>
    /// Normalises token streams and maps them to mood descriptors, intensity modifiers, and valence.
    /// </summary>
    public static class CompanionMoodInterpreter
    {
        /// <summary>Internal description of a mood family entry.</summary>
        private readonly struct MoodDefinition
        {
            public MoodDefinition(
                string descriptor,
                CompanionMoodValence valence,
                CompanionMoodIntensity baselineIntensity,
                string negatedDescriptor = null)
            {
                Descriptor = descriptor ?? string.Empty;
                Valence = valence;
                BaselineIntensity = baselineIntensity;
                NegatedDescriptor = negatedDescriptor ?? string.Empty;
            }

            public string Descriptor { get; }

            public CompanionMoodValence Valence { get; }

            public CompanionMoodIntensity BaselineIntensity { get; }

            public string NegatedDescriptor { get; }
        }

        /// <summary>Lookup table mapping normalised mood tokens to descriptors and metadata.</summary>
        private static readonly Dictionary<string, MoodDefinition> MoodFamilies = new Dictionary<string, MoodDefinition>(StringComparer.Ordinal)
        {
            { "tired", new MoodDefinition("feeling tired", CompanionMoodValence.Negative, CompanionMoodIntensity.Medium) },
            { "sleepy", new MoodDefinition("a bit sleepy", CompanionMoodValence.Negative, CompanionMoodIntensity.Low) },
            { "exhausted", new MoodDefinition("utterly exhausted", CompanionMoodValence.Negative, CompanionMoodIntensity.High) },
            { "drained", new MoodDefinition("feeling drained", CompanionMoodValence.Negative, CompanionMoodIntensity.Medium) },
            { "burnedout", new MoodDefinition("feeling burned out", CompanionMoodValence.Negative, CompanionMoodIntensity.High) },
            { "sad", new MoodDefinition("feeling down", CompanionMoodValence.Negative, CompanionMoodIntensity.Medium) },
            { "upset", new MoodDefinition("upset", CompanionMoodValence.Negative, CompanionMoodIntensity.Medium) },
            { "angry", new MoodDefinition("a little fired up", CompanionMoodValence.Negative, CompanionMoodIntensity.Medium) },
            { "annoyed", new MoodDefinition("annoyed", CompanionMoodValence.Negative, CompanionMoodIntensity.Low) },
            { "frustrated", new MoodDefinition("frustrated", CompanionMoodValence.Negative, CompanionMoodIntensity.Medium) },
            { "worried", new MoodDefinition("a bit worried", CompanionMoodValence.Negative, CompanionMoodIntensity.Medium) },
            { "nervous", new MoodDefinition("a little nervous", CompanionMoodValence.Negative, CompanionMoodIntensity.Low) },
            { "anxious", new MoodDefinition("feeling anxious", CompanionMoodValence.Negative, CompanionMoodIntensity.Medium) },
            { "stressed", new MoodDefinition("feeling stressed", CompanionMoodValence.Negative, CompanionMoodIntensity.Medium) },
            { "bad", new MoodDefinition("feeling bad", CompanionMoodValence.Negative, CompanionMoodIntensity.Medium, "doing not bad") },
            { "rough", new MoodDefinition("having a rough time", CompanionMoodValence.Negative, CompanionMoodIntensity.Medium) },
            { "hurt", new MoodDefinition("feeling hurt", CompanionMoodValence.Negative, CompanionMoodIntensity.Medium) },
            { "sick", new MoodDefinition("feeling sick", CompanionMoodValence.Negative, CompanionMoodIntensity.Medium) },
            { "fedup", new MoodDefinition("feeling fed up", CompanionMoodValence.Negative, CompanionMoodIntensity.Medium) },
            { "lonely", new MoodDefinition("feeling lonely", CompanionMoodValence.Negative, CompanionMoodIntensity.Medium) },
            { "good", new MoodDefinition("doing good", CompanionMoodValence.Positive, CompanionMoodIntensity.Medium, "not feeling good") },
            { "great", new MoodDefinition("feeling great", CompanionMoodValence.Positive, CompanionMoodIntensity.Medium) },
            { "awesome", new MoodDefinition("feeling awesome", CompanionMoodValence.Positive, CompanionMoodIntensity.High) },
            { "happy", new MoodDefinition("happy", CompanionMoodValence.Positive, CompanionMoodIntensity.Medium) },
            { "excited", new MoodDefinition("excited", CompanionMoodValence.Positive, CompanionMoodIntensity.High) },
            { "pumped", new MoodDefinition("pumped", CompanionMoodValence.Positive, CompanionMoodIntensity.High) },
            { "chill", new MoodDefinition("feeling chill", CompanionMoodValence.Neutral, CompanionMoodIntensity.Low) },
            { "calm", new MoodDefinition("feeling calm", CompanionMoodValence.Neutral, CompanionMoodIntensity.Low) },
            { "okay", new MoodDefinition("doing okay", CompanionMoodValence.Neutral, CompanionMoodIntensity.Low) },
            { "ok", new MoodDefinition("doing okay", CompanionMoodValence.Neutral, CompanionMoodIntensity.Low) },
            { "fine", new MoodDefinition("feeling fine", CompanionMoodValence.Neutral, CompanionMoodIntensity.Low) },
            { "alright", new MoodDefinition("feeling alright", CompanionMoodValence.Neutral, CompanionMoodIntensity.Low) },
            { "neutral", new MoodDefinition("steady", CompanionMoodValence.Neutral, CompanionMoodIntensity.Low) }
        };

        /// <summary>Lookup table of terms that amplify or soften mood intensity.</summary>
        private static readonly Dictionary<string, int> IntensityModifiers = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "really", 1 },
            { "very", 1 },
            { "super", 2 },
            { "mega", 2 },
            { "extremely", 2 },
            { "incredibly", 2 },
            { "so", 1 },
            { "totally", 1 },
            { "pretty", 1 },
            { "quite", 1 },
            { "kinda", -1 },
            { "kindof", -1 },
            { "sortof", -1 },
            { "slightly", -1 },
            { "barely", -1 },
            { "little", -1 },
            { "somewhat", -1 }
        };

        /// <summary>Collection of negation tokens that invert or soften the resolved mood.</summary>
        private static readonly HashSet<string> NegationTerms = new HashSet<string>(StringComparer.Ordinal)
        {
            "not",
            "never",
            "no",
            "dont",
            "isnt",
            "arent",
            "wasnt",
            "werent",
            "cant",
            "couldnt"
        };

        /// <summary>Regex patterns used to collapse common multi-word phrases into single lookup tokens.</summary>
        private static readonly Dictionary<Regex, string> MultiWordExpressions = new Dictionary<Regex, string>
        {
            { new Regex(@"\\bkind\\s+of\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "kinda" },
            { new Regex(@"\\bsort\\s+of\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "sortof" },
            { new Regex(@"\\ba\\s+little\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "slightly" },
            { new Regex(@"\\bburned\\s+out\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "burnedout" },
            { new Regex(@"\\bfed\\s+up\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "fedup" }
        };

        /// <summary>
        /// Interprets the provided tokens, returning the detected mood descriptor and intensity.
        /// </summary>
        public static CompanionMoodInterpretation Interpret(IReadOnlyList<string> tokens)
        {
            if (tokens == null || tokens.Count == 0)
                return CompanionMoodInterpretation.Empty;

            var normalised = NormaliseTokens(tokens);
            if (normalised.Count == 0)
                return CompanionMoodInterpretation.Empty;

            int intensityScore = 0;
            bool explicitIntensity = false;
            bool negated = false;
            MoodDefinition? matchedMood = null;

            for (int i = 0; i < normalised.Count; i++)
            {
                string token = normalised[i];
                if (string.IsNullOrEmpty(token))
                    continue;

                if (IntensityModifiers.TryGetValue(token, out int modifier))
                {
                    intensityScore += modifier;
                    explicitIntensity = true;
                    continue;
                }

                if (NegationTerms.Contains(token))
                {
                    negated = !negated;
                    continue;
                }

                if (MoodFamilies.TryGetValue(token, out var definition))
                {
                    matchedMood = definition;
                    break;
                }

                string stem = StemToken(token);
                if (!string.IsNullOrEmpty(stem) && MoodFamilies.TryGetValue(stem, out definition))
                {
                    matchedMood = definition;
                    break;
                }
            }

            if (!matchedMood.HasValue)
                return CompanionMoodInterpretation.Empty;

            var moodDefinition = matchedMood.Value;
            CompanionMoodIntensity intensity = ApplyIntensity(moodDefinition.BaselineIntensity, intensityScore);
            CompanionMoodValence valence = moodDefinition.Valence;
            string descriptor = moodDefinition.Descriptor;

            if (negated)
            {
                descriptor = string.IsNullOrEmpty(moodDefinition.NegatedDescriptor)
                    ? $"not {descriptor}"
                    : moodDefinition.NegatedDescriptor;

                valence = valence switch
                {
                    CompanionMoodValence.Positive => CompanionMoodValence.Negative,
                    CompanionMoodValence.Negative => CompanionMoodValence.Neutral,
                    _ => CompanionMoodValence.Neutral
                };

                if (valence == CompanionMoodValence.Neutral && intensity == CompanionMoodIntensity.High)
                    intensity = CompanionMoodIntensity.Medium;
            }

            return new CompanionMoodInterpretation(descriptor, valence, intensity, negated, explicitIntensity);
        }

        /// <summary>
        /// Applies regex rewrites to collapse multi-word expressions and return a normalised token list.
        /// </summary>
        private static List<string> NormaliseTokens(IReadOnlyList<string> rawTokens)
        {
            var buffer = string.Join(" ", rawTokens);
            if (string.IsNullOrWhiteSpace(buffer))
                return new List<string>();

            string collapsed = buffer;
            foreach (var pair in MultiWordExpressions)
                collapsed = pair.Key.Replace(collapsed, pair.Value);

            var split = collapsed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<string>(split.Length);
            for (int i = 0; i < split.Length; i++)
            {
                string token = split[i].Trim();
                if (!string.IsNullOrEmpty(token))
                    token = token.ToLowerInvariant();
                if (!string.IsNullOrEmpty(token))
                    result.Add(token);
            }

            return result;
        }

        /// <summary>Reduces a token to a simpler stem for lookup fallbacks.</summary>
        private static string StemToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return string.Empty;

            string result = token;

            if (result.Length > 4 && result.EndsWith("ing", StringComparison.Ordinal))
                result = result.Substring(0, result.Length - 3);
            else if (result.Length > 5 && result.EndsWith("ness", StringComparison.Ordinal))
                result = result.Substring(0, result.Length - 4);
            else if (result.Length > 4 && result.EndsWith("ly", StringComparison.Ordinal))
                result = result.Substring(0, result.Length - 2);
            else if (result.Length > 3 && result.EndsWith("ed", StringComparison.Ordinal))
                result = result.Substring(0, result.Length - 2);

            if (result.Length < 2 || string.Equals(result, token, StringComparison.Ordinal))
                return string.Empty;

            return result;
        }

        /// <summary>Clamps the baseline intensity after applying modifiers.</summary>
        private static CompanionMoodIntensity ApplyIntensity(CompanionMoodIntensity baseline, int modifier)
        {
            int baselineScore = baseline == CompanionMoodIntensity.Unknown ? (int)CompanionMoodIntensity.Medium : (int)baseline;
            int adjusted = baselineScore + modifier;
            adjusted = Clamp(adjusted, (int)CompanionMoodIntensity.Low, (int)CompanionMoodIntensity.High);
            return (CompanionMoodIntensity)adjusted;
        }

        /// <summary>Simple integer clamp helper to avoid repeated math utility code.</summary>
        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}
