using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace MarketPlace.Data
{
    public class Item
    {
        public string id { get; set; }
        public int Price { get; set; }
        public string CurrencyID { get; set; }
        public string SkinID { get; set; }
        public string SkinClass { get; set; }
        public string SkinCollection { get; set; }  
        public string CustomerID { get; set; }
        public string PlayfabID { get; set; }
        public bool IsSold { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }

    }
}
