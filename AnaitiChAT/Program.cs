using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using static System.Environment;

const string basePath = @".";

var endpoint = GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
var key = GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

if (string.IsNullOrEmpty(endpoint)
|| string.IsNullOrEmpty(key))
    throw new ArgumentException("AZURE_OPENAI_ENDPOINT and AZURE_OPENAI_API_KEY env variables must be provided");

static string GetCurrentDateTime()
{
    return DateTime.Now.ToString("o");
}

static string GetEmployeesWishesCSV()
{
    return File.ReadAllText(Path.Combine(basePath, "employees_wishes.csv"), Encoding.UTF8);
}

static string GetOvertimesCSV()
{
    return File.ReadAllText(Path.Combine(basePath, "overtimes.csv"), Encoding.UTF8);
}

static string GetScheduledWeekendsCSV()
{
    return File.ReadAllText(Path.Combine(basePath, "scheduled_weekends.csv"), Encoding.UTF8);
}

static string GetPlanCSV()
{
    return File.ReadAllText(Path.Combine(basePath, "plan.csv"), Encoding.UTF8);
}

static string GetAbsencesJSON(string? typeOfAbsence, string? employeeName, string? employeeId, DateTime? absenceStartDate, DateTime? absenceEndDate)
{
    var data = File.ReadAllText(Path.Combine(basePath, "absences.json"), Encoding.UTF8);
    var json = JsonDocument.Parse(data);
    var res = new JsonArray();

    foreach (var item in json.RootElement.EnumerateArray())
    {
        var filteredOut = false;

        if (!filteredOut
        && (typeOfAbsence != null)
        && item.TryGetProperty("Prombūtnes veids", out var itemTypeOfAbsence))
            filteredOut = string.Compare(itemTypeOfAbsence.GetString(), typeOfAbsence, ignoreCase: true) != 0;
        
        if (!filteredOut
        && (employeeName != null)
        && item.TryGetProperty("Darbinieka vārds un uzvārds", out var itemEmployeeName))
            filteredOut = !itemEmployeeName.GetString()?.Contains(employeeName, StringComparison.CurrentCultureIgnoreCase) ?? false;

        if (!filteredOut
        && (employeeId != null)
        && item.TryGetProperty("Darbinieka ID", out var itemEmployeeId))
            filteredOut = string.Compare(itemEmployeeId.GetString(), employeeId, ignoreCase: true) != 0;

        if (!filteredOut
        && (absenceStartDate != null)
        && item.TryGetProperty("Prombūtnes datums", out var itemAbsenceDate))
            filteredOut = absenceStartDate >= itemAbsenceDate.GetDateTime();

        if (!filteredOut
        && (absenceEndDate != null)
        && item.TryGetProperty("Prombūtnes datums", out var itemAbsenceDate2))
            filteredOut = absenceEndDate <= itemAbsenceDate2.GetDateTime();

        if (!filteredOut)
            res.Add(item);
    }

    return res.ToJsonString();
}

static string GetToolCallContent(ChatToolCall toolCall)
{
    if (toolCall.FunctionName == nameof(GetCurrentDateTime))
    {
        return GetCurrentDateTime();
    }
    else if (toolCall.FunctionName == nameof(GetEmployeesWishesCSV))
    {
        return GetEmployeesWishesCSV();
    }
    else if (toolCall.FunctionName == nameof(GetOvertimesCSV))
    {
        return GetOvertimesCSV();
    }
    else if (toolCall.FunctionName == nameof(GetScheduledWeekendsCSV))
    {
        return GetScheduledWeekendsCSV();
    }
    else if (toolCall.FunctionName == nameof(GetPlanCSV))
    {
        return GetPlanCSV();
    }
    else if (toolCall.FunctionName == nameof(GetAbsencesJSON))
    {
        try
        {
            using var args = JsonDocument.Parse(toolCall.FunctionArguments);
            string? typeOfAbsence = null;
            string? employeeId = null;
            string? employeeName = null;
            DateTime? absenceStartDate = null;
            DateTime? absenceEndDate = null;
            
            if (args.RootElement.TryGetProperty("typeOfAbsence", out var typeOfAbsenceJson)) {
                typeOfAbsence = typeOfAbsenceJson.GetString() switch {
                    "vacation" => "Atvaļinājums",
                    "business_trip" => "Komandējums",
                    "sick_leave" => "Slimības lapa",
                    "not_in_employment" => "Nav darba attiecībās",
                    _ => null
                };
            }

            if (args.RootElement.TryGetProperty("employeeName", out var employeeNameJson))
                employeeName = employeeNameJson.GetString();

            if (args.RootElement.TryGetProperty("employeeId", out var employeeIdJson))
                employeeId = employeeIdJson.GetString();

            if (args.RootElement.TryGetProperty("absenceStartDate", out var absenceStartDateJson))
                absenceStartDate = absenceStartDateJson.GetDateTime();
            
            if (args.RootElement.TryGetProperty("absenceEndDate", out var absenceEndDateJson))
                absenceEndDate = absenceEndDateJson.GetDateTime();

            return GetAbsencesJSON(typeOfAbsence, employeeName, employeeId, absenceStartDate, absenceEndDate);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // Handle unexpected tool calls
    throw new NotImplementedException();
}

AzureOpenAIClient azureClient = new(
    new Uri(endpoint),
    new AzureKeyCredential(key));

// This must match the custom deployment name you chose for your model
ChatClient chatClient = azureClient.GetChatClient("AnalitiChAT");

var getCurrentDateTimeTool = ChatTool.CreateFunctionTool(
    functionName: nameof(GetCurrentDateTime),
    functionDescription: "Get the user's current date and time"
);

var getEmployeesWishesCsvTool = ChatTool.CreateFunctionTool(
    functionName: nameof(GetEmployeesWishesCSV),
    functionDescription: "Get the CSV table with employees' wishes (Vārds,Uzvārds,Datums,Dienas tips(Wish),Sākums,Beigas)"
);

var getOvertimesCsvTool = ChatTool.CreateFunctionTool(
    functionName: nameof(GetOvertimesCSV),
    functionDescription: "Get the CSV table with employees' overtimes (Vārds,Uzvārds,Virsstundu skaits)"
);

var getScheduledWeekendsCsvTool = ChatTool.CreateFunctionTool(
    functionName: nameof(GetScheduledWeekendsCSV),
    functionDescription: "Get the CSV table with employees' scheduled weekends (Vārds,Uzvārds,Datums...)"
);

var getPlanCsvTool = ChatTool.CreateFunctionTool(
    functionName: nameof(GetPlanCSV),
    functionDescription: "Get the CSV table with employee schedules (Plāns) (Vārds, Uzvārds,Amats,Slodze,Tabeles nr.,Virsstundas,Normas stundas,Stundas,Nakts stundas,Svētku stundas,Saplānotās stundas)"
);

var getAbsencesJsonTool = ChatTool.CreateFunctionTool(
    functionName: nameof(GetAbsencesJSON),
    functionDescription: "Get the JSON with employee absences, data is filtered using AND operator, all arguments are optional (Prombūtnes veids,Darbinieka vārds un uzvārds,Darbinieka ID,Prombūtnes datums)",
    functionParameters: BinaryData.FromString("""
    {
        "type": "object",
        "properties": {
            "typeOfAbsence": {
                "type": "string",
                "enum": [ "vacation", "business_trip", "sick_leave", "not_in_employment" ],
                "description": "Type of absence"
            },
            "employeeName": {
                "type": "string",
                "description": "Employee name surname, filtered using 'contains'"
            },
            "employeeId": {
                "type": "string",
                "description": "Employee ID number (ID-<num>)"
            },
            "absenceStartDate": {
                "type": "string",
                "description": "Start date of absence, inclusive (yyyy-mm-dd)"
            },
            "absenceEndDate": {
                "type": "string",
                "description": "End date of absence, inclusive (yyyy-mm-dd)"
            }
        },
        "required": [ ]
    }
    """)
);

ChatCompletionOptions options = new()
{
    Tools = { getCurrentDateTimeTool, getEmployeesWishesCsvTool, getOvertimesCsvTool, getScheduledWeekendsCsvTool, getPlanCsvTool, getAbsencesJsonTool },
};

var sysMessage = new SystemChatMessage(
    "You are a helpful assistant. "
    + "Your name is AnalitiChAT. "
    + "Current year is " + DateTime.Now.ToString("yyyy") + ". "
    + "Always respond in the same human language that user had used in the last request."
);
List<ChatMessage> conversationMessages = [sysMessage];

while (true)
{
    Console.Write("User: ");
    var request = Console.ReadLine();

    if (request == "q")
    {
        Console.WriteLine("Quit requested");
        break;
    }
    
    if (request == "c")
    {
        conversationMessages = [sysMessage];
        Console.WriteLine("Cleared message history");
        continue;
    }

    conversationMessages.Add(new UserChatMessage(request));

    ChatCompletion completion = await chatClient.CompleteChatAsync(conversationMessages, options);

    while (completion.FinishReason == ChatFinishReason.ToolCalls)
    {
        // Add a new assistant message to the conversation history that includes the tool calls
        conversationMessages.Add(new AssistantChatMessage(completion));

        foreach (ChatToolCall toolCall in completion.ToolCalls)
        {
            Console.WriteLine("ToolCall: " + toolCall.FunctionName + "(" + toolCall.FunctionArguments + ")");
            conversationMessages.Add(new ToolChatMessage(toolCall.Id, GetToolCallContent(toolCall)));
        }

        // Now make a new request with all the messages thus far, including the original
        completion = await chatClient.CompleteChatAsync(conversationMessages, options);
    }

    Console.WriteLine($"{completion.Role}: {completion.Content[0].Text}");
}