using System.Net;
using System.Text.Json.Nodes;
using Azure;
using Azure.AI.OpenAI;

namespace WebMVC.Views.Concierge;

public class ChatState
{
    string aoaiEndpoint = Environment.GetEnvironmentVariable("AZUREOPENAI_ENDPOINT")!;
    string aoaiApiKey = Environment.GetEnvironmentVariable("AZUREOPENAI_API_KEY")!;
    string aoaiModel = "Gpt35Turbo_16k";

    public const string SearchCatalogFunctionName = "search_catalog";
    public const string AddToBasketFunctionName = "add_to_basket";

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

#if !FAKE_OPENAI
        _client = new OpenAIClient(new Uri(aoaiEndpoint), new AzureKeyCredential(aoaiApiKey));
#endif
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
                var response = await _client.GetChatCompletionsAsync(aoaiModel, _completionsOptions);
                choice = response.Value.Choices[0];

                if (choice.FinishReason != CompletionsFinishReason.FunctionCall)
                {
                    _completionsOptions.Messages.Add(choice.Message);
                    onMessageAdded();
                    return;
                }

                switch (choice.Message.FunctionCall.Name)
                {
                    case SearchCatalogFunctionName:
                        _completionsOptions.Messages.Add(new ChatMessage(ChatRole.Assistant, "Searching the catalog..."));
                        onMessageAdded();

                        string productDescription = JsonNode.Parse(choice.Message.FunctionCall.Arguments)?["product_description"]?.ToString();
                        try
                        {
                            productDescription = await new HttpClient().GetStringAsync($"http://webshoppingagg/c/api/v1/catalog/items/withsemantic/{WebUtility.UrlEncode(productDescription)}?pageSize=3&pageIndex=0");
                        }
                        catch (HttpRequestException)
                        {
                            productDescription = "Error accessing catalog.";
                        }
                        _completionsOptions.Messages.Add(new ChatMessage(ChatRole.Function, productDescription) { Name = SearchCatalogFunctionName });
                        onMessageAdded();
                        continue;

                    case AddToBasketFunctionName:
                        // TODO: Call basket endpoint
                        _completionsOptions.Messages.Add(new ChatMessage(ChatRole.Function, "Item added to shopping cart.") { Name = AddToBasketFunctionName });
                        onMessageAdded();
                        continue;

                        // TODO: Could add other functions, e.g.
                        // - Enumerate basket
                        // - Remove from basket
                }
            }
        }
        catch (Exception ex)
        {
            _completionsOptions.Messages.Add(new ChatMessage(ChatRole.Assistant, "My apologies, but I encountered an unexpected error: " + ex.ToString()));
        }
    }
}
