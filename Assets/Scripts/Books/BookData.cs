using System;
using System.Collections.Generic;
using UnityEngine;

namespace Books
{
    [CreateAssetMenu(menuName = "Books/BookData")]
    public class BookData : ScriptableObject
    {
        public string id;
        public string title;
        public string content;

        [Obsolete("Use content instead")]
        public List<string> pages = new List<string>();

        private const string PageSplit = "\f";

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(content) && pages != null && pages.Count > 0)
            {
                content = string.Join(PageSplit, pages);
            }
        }

        public IEnumerable<string> GetPages()
        {
            if (!string.IsNullOrEmpty(content))
            {
                return content.Split(new[] { PageSplit }, StringSplitOptions.None);
            }

            return pages ?? new List<string>();
        }
    }
}
