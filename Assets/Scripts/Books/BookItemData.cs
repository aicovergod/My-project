using Inventory;
using UnityEngine;

namespace Books
{
    [CreateAssetMenu(menuName = "Inventory/BookItemData")]
    public class BookItemData : ItemData
    {
        public BookData book;
    }
}
