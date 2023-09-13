using System.Text.Json.Nodes;
using Azure;
using Azure.AI.OpenAI;

namespace WebMVC.Views.Concierge;

public class ChatState
{
    private static readonly string s_aoaiEndpoint = Environment.GetEnvironmentVariable("ESHOP_AZURE_OPENAI_ENDPOINT")!;
    private static readonly string s_aoaiApiKey = Environment.GetEnvironmentVariable("ESHOP_AZURE_OPENAI_API_KEY")!;
    private static readonly string s_aoaiModel = "Gpt35Turbo_16k";

    private readonly OpenAIClient _client;
    private readonly ChatCompletionsOptions _completionsOptions;
    private readonly ICatalogService _catalogService;
    private readonly IBasketService _basketService;
    private readonly ApplicationUser _user;

    public ChatState(ICatalogService catalogService, IBasketService basketService, ApplicationUser user)
    {
        _catalogService = catalogService;
        _basketService = basketService;
        _user = user;

        _client = new OpenAIClient(new Uri(s_aoaiEndpoint), new AzureKeyCredential(s_aoaiApiKey));
        _completionsOptions = new();

        _completionsOptions.Functions.Add(new FunctionDefinition("get_user_info")
        {
            Description = "Gets information about the chat user",
            Parameters = BinaryData.FromString("""
                                               {
                                                   "type":"object",
                                                   "properties":{},
                                                   "required":[]
                                               }
                                               """)
        });

        _completionsOptions.Functions.Add(new FunctionDefinition("search_catalog")
        {
            Description = "Searches the eShop catalog for a provided product description",
            Parameters = BinaryData.FromString("""
                                               {
                                                   "type":"object",
                                                   "properties":{
                                                       "product_description":{ 
                                                           "type":"string",
                                                           "description":"The product description for which to search"
                                                       }
                                                   },
                                                   "required":["product_description"]
                                               }
                                               """)
        });

        _completionsOptions.Functions.Add(new FunctionDefinition("add_to_cart")
        {
            Description = "Adds a product to the user's shopping cart.",
            Parameters = BinaryData.FromString("""
                                               {
                                                   "type":"object",
                                                   "properties":{
                                                       "id":{ 
                                                           "type":"string",
                                                           "description":"The id of the product to add to the shopping cart (basket)."
                                                       }
                                                   },
                                                   "required":["id"]
                                               }
                                               """)
        });

        _completionsOptions.Functions.Add(new FunctionDefinition("get_cart_contents")
        {
            Description = "Gets information about the contents of the user's shopping cart (basket)",
            Parameters = BinaryData.FromString("""
                                               {
                                                   "type":"object",
                                                   "properties":{},
                                                   "required":[]
                                               }
                                               """)
        });

        _completionsOptions.Messages.Add(new ChatMessage(ChatRole.System, """
            You are an AI customer service agent for the online retailer eShop.
            eShop primarily sells products related to .NET, in particular clothing apparel, mugs, and pins.
            Your job is to answer customer questions about products in the eShop catalog.
            You are polite, helpful, and knowledgeable about the eShop catalog.
            You try to be concise in your responses, but you will provide a longer response if needed or asked.
            You limit your responses to only include information about eShop, and you avoid discussing other topics.
            """));

        _completionsOptions.Messages.Add(new ChatMessage(ChatRole.Assistant, "Hi! I'm the .NET Concierge. How can I help?"));
    }

    public IList<ChatMessage> Messages => _completionsOptions.Messages;

    public async Task AddUserMessageAsync(string userText, Action onMessageAdded)
    {
        _completionsOptions.Messages.Add(new ChatMessage(ChatRole.User, userText));
        onMessageAdded();

        try
        {
            ChatChoice choice;
            while (true)
            {
                var response = await _client.GetChatCompletionsAsync(s_aoaiModel, _completionsOptions);
                choice = response.Value.Choices[0];

                if (choice.FinishReason != CompletionsFinishReason.FunctionCall)
                {
                    _completionsOptions.Messages.Add(choice.Message);
                    onMessageAdded();
                    return;
                }

                switch (choice.Message.FunctionCall.Name)
                {
                    case "get_user_info":
                        _completionsOptions.Messages.Add(new ChatMessage(ChatRole.Function, JsonSerializer.Serialize(_user)) { Name = "get_user_info" });
                        onMessageAdded();
                        continue;

                    case "search_catalog":
                        _completionsOptions.Messages.Add(new ChatMessage(ChatRole.Assistant, "Searching the catalog..."));
                        onMessageAdded();

                        string productDescription = JsonNode.Parse(choice.Message.FunctionCall.Arguments)?["product_description"]?.ToString();
                        try
                        {
                            var results = await _catalogService.GetCatalogItems(0, 3, productDescription);
                            productDescription = JsonSerializer.Serialize(results);
                        }
                        catch (HttpRequestException)
                        {
                            productDescription = "Error accessing catalog.";
                        }
                        _completionsOptions.Messages.Add(new ChatMessage(ChatRole.Function, productDescription) { Name = "search_catalog" });
                        onMessageAdded();
                        continue;

                    case "add_to_cart":
                        await _basketService.AddItemToBasket(_user, int.Parse(JsonNode.Parse(choice.Message.FunctionCall.Arguments)?["id"]?.ToString()));
                        _completionsOptions.Messages.Add(new ChatMessage(ChatRole.Function, "Item added to shopping cart.") { Name = "add_to_cart" });
                        onMessageAdded();
                        continue;

                    case "get_cart_contents":
                        Basket b = await _basketService.GetBasket(_user);
                        _completionsOptions.Messages.Add(new ChatMessage(ChatRole.Function, JsonSerializer.Serialize(b.Items)) { Name = "get_cart_contents" });
                        onMessageAdded();
                        continue;
                }
            }
        }
        catch (Exception ex)
        {
            _completionsOptions.Messages.Add(new ChatMessage(ChatRole.Assistant, $"My apologies, but I encountered an unexpected error: {ex}"));
        }
    }
}
