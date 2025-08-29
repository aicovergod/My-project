using System.Collections.Generic;
using UnityEngine;

namespace Books
{
    [CreateAssetMenu(menuName = "Books/BookData")]
    public class BookData : ScriptableObject
    {
        public string id;
        public string title;
        public List<string> pages = new List<string>();
    }
}
