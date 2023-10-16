using CsvHelper.Configuration;

namespace ProductsChatbot.Models
{
    public class Product
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public string Description { get; set; }
        public string Price { get; set; }
    }

    public class ProductMap : ClassMap<Product>
    {
        public ProductMap()
        {
            Map(m => m.ProductId).Name("product_id");
            Map(m => m.ProductName).Name("product_name");
            Map(m => m.Description).Name("description");
            Map(m => m.Price).Name("price");
        }
    }
}
