open FSharp.Configuration
open System.Text.Json
open System.Net.Http
open System.Collections.Generic
open Common
open Giraffe.ViewEngine
open Views
open System.IO

type Config = YamlConfig<"site_config.yml">

type ChartDto =
    { chart_name: string
      chart_sequence: string
      pdf_name: string
      pdf_url: string
      did_change: bool }

type ChartsByType =
    { airport_diagram: ChartDto list option
      general: ChartDto list option
      departure: ChartDto list option
      arrival: ChartDto list option
      approach: ChartDto list option }

type AirportData =
    { city: string
      state_abbr: string
      state_full: string
      country: string
      icao_ident: string
      faa_ident: string
      airport_name: string
      is_military: bool }

type ChartsResponse =
    { airport_data: AirportData
      charts: ChartsByType }

type ChartResult = string * ChartsResponse

let getAllAirports (config: Config) =
    [ Seq.map (fun a -> { Id = a; Class = Bravo }) config.Airports.Bravo
      Seq.map (fun a -> { Id = a; Class = Charlie }) config.Airports.Charlie
      Seq.map (fun a -> { Id = a; Class = Delta }) config.Airports.Delta ]
    |> Seq.collect id

let makeUrl airports =
    let airportsQueryString =
        airports
        |> Seq.map (fun a -> $"K{a}")
        |> String.concat ","
    $"https://api-v2.aviationapi.com/v2/charts?airport={airportsQueryString}"

let fetchCharts (url: string) =
    task {
        let httpClient = new HttpClient()
        let! response = httpClient.GetAsync(url)
        let! stream = response.Content.ReadAsStreamAsync()
        let options = JsonSerializerOptions()
        options.PropertyNameCaseInsensitive <- true
        let! doc = JsonDocument.ParseAsync(stream)
        let results = ResizeArray<ChartResult>()

        for prop in doc.RootElement.EnumerateObject() do
            let charts = JsonSerializer.Deserialize<ChartsResponse>(prop.Value.GetRawText(), options)
            results.Add((prop.Name, charts))

        return List.ofSeq results
    }

let parseChart (chartType: string) (chart: ChartDto) =
    match parseChartType chartType with
    | Some t ->
        Some
            { Airport = "" // Could optionally set from airport metadata
              Name = chart.chart_name
              Type = t
              PdfPath = chart.pdf_url }
    | None -> None

let mapCharts (airports: Airport seq) (results: ChartResult list) =
    let sanitizeCharts (chartType: string) (charts: ChartDto list option) =
        match charts with
        | Some cs ->
            cs
            |> List.map (parseChart chartType)
            |> List.choose id
        | None -> []

    let flattenCharts (airportId: string, response: ChartsResponse) =
        [
            sanitizeCharts "airport_diagram" response.charts.airport_diagram
            sanitizeCharts "general" response.charts.general
            sanitizeCharts "departure" response.charts.departure
            sanitizeCharts "arrival" response.charts.arrival
            sanitizeCharts "approach" response.charts.approach
        ]
        |> List.collect id
        |> List.distinctBy (fun c -> (c.Name, c.PdfPath))
        |> List.sortBy chartToInt

    let chartMap =
        results
        |> List.map (fun (id, r) -> (id, flattenCharts (id, r)))
        |> Map.ofList

    airports
    |> Seq.map (fun a -> (a, Map.tryFind a.Id chartMap |> Option.defaultValue []))
    |> Seq.filter (fun (_, c) -> not c.IsEmpty)

[<EntryPoint>]
let main args =
    let outputDir =
        match Array.toList args with
        | [] -> "./"
        | arg :: _ -> arg.TrimEnd([| '/'; '\\' |])

    let config = Config()
    let airports = getAllAirports config
    let chartsResponse =
        airports
        |> Seq.map (fun a -> a.Id)
        |> makeUrl
        |> fetchCharts
        |> Async.AwaitTask
        |> Async.RunSynchronously

    let viewModel = mapCharts airports chartsResponse

    if not (Directory.Exists(outputDir)) then
        Directory.CreateDirectory(outputDir) |> ignore

    renderPage config.Title viewModel
    |> RenderView.AsString.htmlDocument
    |> fun s -> File.WriteAllText($"{outputDir}/index.html", s)

    0
