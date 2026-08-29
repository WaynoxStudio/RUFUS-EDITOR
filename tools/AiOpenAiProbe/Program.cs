using RufusMapEditor.AiBackend;
using RufusMapEditor.AiBackend.OpenAi;
using RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.4B — manual OpenAI probe. Does NOT run a real call unless --real is passed
/// and OPENAI_API_KEY is set. Never writes results into an NPC draft.
/// </summary>
if (!args.Contains("--real", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("AiOpenAiProbe");
    Console.WriteLine("Sin --real: no se llama a OpenAI.");
    Console.WriteLine();
    Console.WriteLine("Uso:");
    Console.WriteLine("  set OPENAI_API_KEY=...");
    Console.WriteLine("  set OPENAI_MODEL=gpt-5-mini   (opcional)");
    Console.WriteLine("  dotnet run --project tools/AiOpenAiProbe -- --real");
    Console.WriteLine();
    Console.WriteLine("Prueba prevista: generate_name · Minero · Gruñón · cueva.");
    return 0;
}

var options = OpenAiOptions.FromEnvironment();
if (!options.IsConfigured)
{
    Console.WriteLine("AI_NOT_CONFIGURED: falta OPENAI_API_KEY en el entorno.");
    return 2;
}

Console.WriteLine($"Modelo: {options.Model}");
Console.WriteLine("Acción: generate_name");
Console.WriteLine("Llamando a OpenAI Responses API (una sola prueba)...");

var creative = AiCreativeRequestBuilder.Build(
    AiCreativeAction.GenerarNombre,
    "Minero",
    null,
    "Gruñón",
    null,
    "Trabaja solo en una cueva.",
    null,
    AiTextLength.Corta,
    null);
var package = AiPromptComposer.Compose(creative);
var request = AiBackendRequestBuilder.Build(creative, package);

using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
var client = new OpenAiResponsesClient(http, options);
var orch = new AiGenerateOrchestrator(options, client);
var response = await orch.GenerateAsync(request, CancellationToken.None);

if (!response.Success)
{
    Console.WriteLine("ERROR");
    Console.WriteLine($"code={response.Error?.Code}");
    Console.WriteLine($"message={response.Error?.Message}");
    return 1;
}

Console.WriteLine("OK — respuesta validada (backend). No aplicada a NPC.");
Console.WriteLine(response.Result?.GetRawText());
return 0;
