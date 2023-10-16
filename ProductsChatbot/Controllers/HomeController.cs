using Cassandra.Mapping;
using Cassandra;
using Microsoft.AspNetCore.Mvc;
using ProductsChatbot.Models;
using System.Diagnostics;
using ISession = Cassandra.ISession;
using CsvHelper.Configuration;
using CsvHelper;
using Microsoft.Extensions.Hosting;
using System.Reflection;
using OpenAI_API.Embedding;
using OpenAI_API.Models;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Collections.Generic;
using OpenAI_API.Images;
using OpenAI_API.Chat;

namespace ProductsChatbot.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ISession _Session;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _Host;

        public HomeController(ILogger<HomeController> logger, IConfiguration configuration, IWebHostEnvironment host)
        {
            _logger = logger;
            _configuration = configuration;
            _Host = host;
            string dirSeparator = Path.DirectorySeparatorChar.ToString();
            string secureConnectionBundle = host.ContentRootPath + dirSeparator + _configuration.GetSection("AstraDb:SecureConnectionBundleName").Get<string>();
            string username = _configuration.GetSection("AstraDb:Username").Get<string>();
            string password = _configuration.GetSection("AstraDb:Password").Get<string>();
            string keyspace = _configuration.GetSection("AstraDb:Keyspace").Get<string>();

            try
            {
                if (_Session == null)
                {
                    //Build connection to Astra and establish connection
                    _Session = (Session)Cluster.Builder()
                        .WithCloudSecureConnectionBundle(secureConnectionBundle)
                        .WithCredentials(username, password)
                        .Build()
                        .Connect(keyspace);
                }
            }
            catch (Exception ex)
            {
                throw;
            }

            // test the connection
            SimpleStatement statement = new SimpleStatement("select count(*) from products_table");
            var results = _Session.Execute(statement);
            Row row = results.FirstOrDefault();


        }

        string GetProductRecommendations(string question)
        {
            List<Product> products = new List<Product>();
            string apiKey = _configuration.GetSection("OPENAI_API_KEY").Get<string>();
            OpenAI_API.OpenAIAPI api = new OpenAI_API.OpenAIAPI(apiKey);

            // get the embedding for the question
            EmbeddingResult embeddingResult = api.Embeddings.CreateEmbeddingAsync(question).Result;
            float[] embedding = embeddingResult.Data[0].Embedding;
            string embeddingString = "[" + string.Join(",", embedding) + "]";

            // Query the vector database for the top five products
            SimpleStatement statement = new SimpleStatement(
                $@"    
                SELECT product_id, product_name, description, price
                FROM vector_preview.products_table
                ORDER BY openai_description_embedding ANN OF { embeddingString } LIMIT 5; 
                "
                );
            var results = _Session.Execute(statement);
            foreach (var row in results)
            {
                Product product = new Product();
                product.ProductId = Convert.ToInt32(row["product_id"]);
                product.ProductName = row["product_name"].ToString();
                product.Description = row["description"].ToString();
                product.Price = row["price"].ToString();
                products.Add(product);
            }

            // Ask OpenAI to provide a prompt engineered response
            ChatRequest chatRequest = new ChatRequest();
            chatRequest.Model = Model.ChatGPTTurbo;
            var chat = api.Chat.CreateConversation(chatRequest);

            chat.AppendSystemMessage("You're a chatbot helping customers with questions and helping them with product recommendations");
            chat.AppendUserInput(question);
            chat.AppendUserInput("Please give me a detailed explanation of your recommendations");
            chat.AppendUserInput("Please be friendly and talk to me like a person, don't just give me a list of recommendations");
            chat.AppendExampleChatbotOutput("I found these 3 products I would recommend");
            foreach (var product in products)
            {
                chat.AppendExampleChatbotOutput(product.Description);
            }
            chat.AppendExampleChatbotOutput("Here's my summarized recommendation of products, and why it would suit you:");
            string response = chat.GetResponseFromChatbot().Result;

            return response;
        }


        [HttpGet]
        public IActionResult GetBotResponse(string userInput)
        {
            //var products = GetProductRecommendations(userInput);

            string botResponse = GetProductRecommendations(userInput); // GetResponse(userInput);
            return Ok(botResponse);
        }

        private string GetResponse(string userInput)
        {
            userInput = userInput.ToLower();

            if (userInput.Contains("hello"))
                return "Hi there! How can I help you today?";
            else if (userInput.Contains("your name"))
                return "I'm ChatGPT Bot!";
            else
                return "Sorry, I didn't understand that.";

        }

        public IActionResult ProcessData()
        {
            // Get the Products
            List<Product> products = GetProducts();

            // Create embeddings
            var test = GetEmbedding("This is a test").Result;

            return RedirectToAction("Index");
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        List<Product> GetProducts()
        {
            string dirSeparator = Path.DirectorySeparatorChar.ToString();
            var filePath = _Host.ContentRootPath + dirSeparator + _configuration.GetSection("ProductDataset").Get<string>();
            
            using (var reader = new StreamReader(filePath))
            using (var csv = new CsvReader(reader, new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)))
            {
                csv.Context.RegisterClassMap<ProductMap>();
                var records = csv.GetRecords<Product>();
                return new List<Product>(records);
            }
        }

        void ProcessAndInsertData(List<Product> products)
        {
            foreach (var product in products)
            {
                int textChunkLength = 2500;
                var textChunks = Enumerable.Range(0, product.Description.Length / textChunkLength)
                                          .Select(i => product.Description.Substring(i * textChunkLength, textChunkLength))
                                          .ToList();

                for (int chunkId = 0; chunkId < textChunks.Count; chunkId++)
                {
                    string chunk = textChunks[chunkId];
                    string priceValue = string.IsNullOrEmpty(product.Price) ? "" : product.Price;
                    string fullChunk = $"{chunk} price: {priceValue}";

                    // Here you should get the embedding, assuming you have a method GetEmbedding() that does this
                    var embedding = GetEmbedding(fullChunk);
                    

                    string cqlQuery = @"
                    INSERT INTO vector_preview.products_table
                    (product_id, chunk_id, product_name, description, price, openai_description_embedding)
                    VALUES (?, ?, ?, ?, ?, ?)
                ";

                    var preparedStatement = _Session.Prepare(cqlQuery);
                    var boundStatement = preparedStatement.Bind(product.ProductId, chunkId, product.ProductName, product.Description, priceValue, embedding);

                    _Session.Execute(boundStatement);
                }
            }
        }

        public async Task<float[]> GetEmbedding(string data)
        {
            EmbeddingResult embeddingResult = null;
            string apiKey = _configuration.GetSection("OPENAI_API_KEY").Get<string>();
            OpenAI_API.OpenAIAPI api = new OpenAI_API.OpenAIAPI(apiKey);

            try
            {
                embeddingResult = await api.Embeddings.CreateEmbeddingAsync(data);

                return embeddingResult.Data[0].Embedding;

            }
            catch (Exception ex)
            {
                return embeddingResult.Data[0].Embedding;
            }
        }

    }
}