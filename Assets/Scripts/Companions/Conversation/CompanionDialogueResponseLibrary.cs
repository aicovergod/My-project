using System;
using System.Collections.Generic;
using UnityEngine;

namespace Companions.Conversation
{
    /// <summary>
    /// Provides selection helpers that bridge the runtime dialogue systems with the static
    /// <see cref="CompanionResponseCatalog"/>. The library applies guard predicates, performs
    /// weighted random selection, and exposes follow-up prompts encoded within templates.
    /// </summary>
    [Serializable]
    public sealed class CompanionDialogueResponseLibrary
    {
        private static readonly char[] FollowUpSeparators = { '|' };

        /// <summary>
        /// Ensures the shared catalog is populated before any queries occur.
        /// </summary>
        public void EnsureDefaults()
        {
            CompanionResponseCatalog.EnsureDefaults();
        }

        /// <summary>
        /// Attempts to select a response template for the supplied intent.
        /// </summary>
        /// <param name="intent">Intent driving the selection.</param>
        /// <param name="context">Runtime context used by guard predicates.</param>
        /// <param name="disallowedTemplateKey">Optional template key that should be avoided.</param>
        /// <param name="selection">Resulting selection containing the raw segments.</param>
        public bool TrySelectResponse(
            CompanionDialogueIntent intent,
            CompanionResponseContext context,
            string disallowedTemplateKey,
            out ResponseSelection selection)
        {
            var templates = CompanionResponseCatalog.GetTemplates(intent);
            if (templates.Count == 0)
            {
                selection = default;
                return false;
            }

            var candidates = new List<CandidateTemplate>(templates.Count);
            for (int i = 0; i < templates.Count; i++)
            {
                var template = templates[i];
                if (template.Guard != null && !template.Guard(context))
                    continue;

                string rawText = template.Text?.Trim();
                if (string.IsNullOrEmpty(rawText))
                    continue;

                if (!string.IsNullOrEmpty(disallowedTemplateKey) &&
                    string.Equals(rawText, disallowedTemplateKey, StringComparison.Ordinal))
                {
                    continue;
                }

                string[] segments = rawText.Split(FollowUpSeparators, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0)
                    continue;

                candidates.Add(new CandidateTemplate(template, segments));
            }

            if (candidates.Count == 0)
            {
                selection = default;
                return false;
            }

            var chosen = ChooseWeighted(candidates);
            var followUps = chosen.Segments.Length > 1
                ? new List<string>(chosen.Segments.Length - 1)
                : null;

            if (followUps != null)
            {
                for (int i = 1; i < chosen.Segments.Length; i++)
                    followUps.Add(chosen.Segments[i].Trim());
            }

            selection = new ResponseSelection(
                chosen.Segments[0].Trim(),
                followUps ?? (IReadOnlyList<string>)Array.Empty<string>(),
                chosen.Template.Text);
            return true;
        }

        private static CandidateTemplate ChooseWeighted(List<CandidateTemplate> candidates)
        {
            if (candidates.Count == 1)
                return candidates[0];

            float totalWeight = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                totalWeight += Mathf.Max(0.0001f, candidates[i].Template.Weight);
            }

            float roll = UnityEngine.Random.Range(0f, totalWeight);
            float cumulative = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                float weight = Mathf.Max(0.0001f, candidates[i].Template.Weight);
                cumulative += weight;
                if (roll <= cumulative)
                    return candidates[i];
            }

            return candidates[candidates.Count - 1];
        }

        private readonly struct CandidateTemplate
        {
            public CandidateTemplate(CompanionResponseCatalog.ResponseTemplate template, string[] segments)
            {
                Template = template;
                Segments = segments;
            }

            public CompanionResponseCatalog.ResponseTemplate Template { get; }

            public string[] Segments { get; }
        }

        /// <summary>
        /// Represents the selected template and any follow-up prompts extracted from it.
        /// </summary>
        public readonly struct ResponseSelection
        {
            public ResponseSelection(string primarySegment, IReadOnlyList<string> followUps, string templateKey)
            {
                PrimarySegment = primarySegment ?? string.Empty;
                FollowUpSegments = followUps ?? Array.Empty<string>();
                TemplateKey = templateKey ?? string.Empty;
            }

            /// <summary>Primary template segment returned to the caller.</summary>
            public string PrimarySegment { get; }

            /// <summary>Additional follow-up prompts encoded within the template.</summary>
            public IReadOnlyList<string> FollowUpSegments { get; }

            /// <summary>Raw template key used to avoid immediate repeats.</summary>
            public string TemplateKey { get; }
        }
    }
}
