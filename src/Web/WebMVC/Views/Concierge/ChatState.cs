using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Components;

namespace WebMVC.Views.Concierge;

public class ChatState
{
    string aoaiEndpoint = Environment.GetEnvironmentVariable("AZUREOPENAI_ENDPOINT")!;
    string aoaiApiKey = Environment.GetEnvironmentVariable("AZUREOPENAI_API_KEY")!;
    string aoaiModel = "Gpt35Turbo_16k";

    const string SearchCatalogFunctionName = "search_catalog";
    const string AddToBasketFunctionName = "add_to_basket";

    private readonly OpenAIClient _client;
    private readonly ChatCompletionsOptions _completionsOptions;

    public ChatState()
    {
        _completionsOptions = new();

        _completionsOptions.Functions.Add(new FunctionDefinition(SearchCatalogFunctionName)
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
             "required": ["product_description"]
         }
         """)
        });

        _completionsOptions.Functions.Add(new FunctionDefinition(AddToBasketFunctionName)
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
             "required": ["id"]
         }
         """)
        });

        _completionsOptions.Messages.Add(new ChatMessage(ChatRole.System, """
            You are an AI customer service agent for the online retailer eShop.
            eShop primarily sells products related to .NET, in particular clothing apparel and mugs.
            Your job is to answer customer questions about products in the eShop catalog.
            You are polite, helpful, and knowledgeable about the eShop catalog.
            You try to be concise in your responses, but you will provide a longer response if needed or asked.
            Limit your responses about products to data available in the catalog.
            """));

        _completionsOptions.Messages.Add(new ChatMessage(ChatRole.Assistant, "Hi! I'm the .NET Concierge. How can I help?"));

        //_client = new OpenAIClient(new Uri(aoaiEndpoint), new AzureKeyCredential(aoaiApiKey));
    }

    public IList<ChatMessage> Messages => _completionsOptions.Messages;

    public async Task AddUserMessageAndGetResponseAsync(string text)
    {
        _completionsOptions.Messages.Add(new ChatMessage(ChatRole.User, text));
        await Task.Delay(1000);
        _completionsOptions.Messages.Add(new ChatMessage(ChatRole.Assistant, "Yeah whatever"));
    }
}
