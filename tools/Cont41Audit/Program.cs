using System.Globalization;
using System.Text;
using System.Text.Json;
using MySqlConnector;
using RufusMapEditor.LegacyCompatibility.Database;

Console.OutputEncoding = Encoding.UTF8;
var settingsPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "RufusMapEditor", "settings.json");
using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
var dbEl = doc.RootElement.GetProperty("Database");
var db = new DatabaseSettings
{
    Host = dbEl.GetProperty("Host").GetString() ?? "",
    Port = dbEl.GetProperty("Port").GetInt32(),
    User = dbEl.GetProperty("User").GetString() ?? "",
    Database = dbEl.GetProperty("Database").GetString() ?? "estaticos",
    PasswordProtectedBase64 = dbEl.GetProperty("PasswordProtectedBase64").GetString(),
};
var schema = string.IsNullOrWhiteSpace(db.Database) ? "estaticos" : db.Database.Trim();
await using var conn = new MySqlConnection(db.BuildConnectionString(DatabasePasswordProtector.Unprotect(db.PasswordProtectedBase64)));
await conn.OpenAsync();

async Task DumpAsync(string title, string sql)
{
    Console.WriteLine("=== " + title + " ===");
    await using var cmd = new MySqlCommand(sql, conn);
    await using var rd = await cmd.ExecuteReaderAsync();
    Console.WriteLine(string.Join(" | ", Enumerable.Range(0, rd.FieldCount).Select(rd.GetName)));
    var rows = 0;
    while (await rd.ReadAsync())
    {
        rows++;
        var cells = new string[rd.FieldCount];
        for (var i = 0; i < rd.FieldCount; i++)
        {
            if (rd.IsDBNull(i)) cells[i] = "NULL";
            else
            {
                var s = Convert.ToString(rd.GetValue(i), CultureInfo.InvariantCulture) ?? "";
                if (s.Length > 180) s = s[..177] + "...";
                cells[i] = s.Replace("\r", "\\r").Replace("\n", "\\n");
            }
        }
        Console.WriteLine(string.Join(" | ", cells));
    }
    Console.WriteLine($"({rows} rows)\n");
}

await DumpAsync("npc_respuestas columns", $@"
SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA='{schema}' AND TABLE_NAME='npc_respuestas' ORDER BY ORDINAL_POSITION");

await DumpAsync("Exists pregunta 20009?", $@"
SELECT COUNT(*) AS n FROM `{schema}`.`npc_preguntas` WHERE id=20009");

await DumpAsync("Exists pregunta 20006/20007?", $@"
SELECT id, respuestas, `params`, alternos FROM `{schema}`.`npc_preguntas` WHERE id IN (20006,20007,20008,20009)");

await DumpAsync("npc_respuestas id IN (20007,20009) OR args=3503", $@"
SELECT orden, id, accion, args, condicion FROM `{schema}`.`npc_respuestas`
WHERE id IN (20007,20009,20006) OR args='3503' LIMIT 40");

await DumpAsync("Responses of q 20006 via FIND_IN_SET", $@"
SELECT p.id AS qid, p.respuestas, r.orden, r.id AS rid, r.accion, r.args, r.condicion
FROM `{schema}`.`npc_preguntas` p
JOIN `{schema}`.`npc_respuestas` r ON FIND_IN_SET(r.id, REPLACE(p.respuestas,' ',''))
WHERE p.id=20006 LIMIT 20");

await DumpAsync("accion=44 count + samples", $@"
SELECT COUNT(*) AS n44 FROM `{schema}`.`npc_respuestas` WHERE accion=44");

await DumpAsync("accion=44 samples", $@"
SELECT orden, id, accion, args, condicion FROM `{schema}`.`npc_respuestas`
WHERE accion=44 ORDER BY orden LIMIT 25");

await DumpAsync("accion=44 args top", $@"
SELECT args, COUNT(*) AS n FROM `{schema}`.`npc_respuestas`
WHERE accion=44 GROUP BY args ORDER BY n DESC LIMIT 25");

await DumpAsync("args=3503 any accion", $@"
SELECT orden, id, accion, args, condicion FROM `{schema}`.`npc_respuestas` WHERE args='3503' LIMIT 20");

await DumpAsync("acciones 40-55 counts", $@"
SELECT accion, COUNT(*) AS n FROM `{schema}`.`npc_respuestas`
WHERE accion BETWEEN 40 AND 55 GROUP BY accion ORDER BY accion");

await DumpAsync("acciones 40-55 samples", $@"
(SELECT orden,id,accion,args FROM `{schema}`.`npc_respuestas` WHERE accion=40 LIMIT 2)
UNION ALL (SELECT orden,id,accion,args FROM `{schema}`.`npc_respuestas` WHERE accion=41 LIMIT 2)
UNION ALL (SELECT orden,id,accion,args FROM `{schema}`.`npc_respuestas` WHERE accion=42 LIMIT 2)
UNION ALL (SELECT orden,id,accion,args FROM `{schema}`.`npc_respuestas` WHERE accion=43 LIMIT 2)
UNION ALL (SELECT orden,id,accion,args FROM `{schema}`.`npc_respuestas` WHERE accion=45 LIMIT 2)
UNION ALL (SELECT orden,id,accion,args FROM `{schema}`.`npc_respuestas` WHERE accion=46 LIMIT 2)
UNION ALL (SELECT orden,id,accion,args FROM `{schema}`.`npc_respuestas` WHERE accion=47 LIMIT 2)");

await DumpAsync("Top acciones", $@"
SELECT accion, COUNT(*) AS n FROM `{schema}`.`npc_respuestas` GROUP BY accion ORDER BY n DESC LIMIT 20");

await DumpAsync("Stage objetivos separators sample", $@"
SELECT id, objetivos, variosobj FROM `{schema}`.`mision_etapas`
WHERE id IN (55,165,166,5500,343,342,432)");

await DumpAsync("Mission 181 etapas order vs ids", $@"
SELECT id, etapas FROM `{schema}`.`misiones` WHERE id=181");

await DumpAsync("tipo14 linked to mission 701?", $@"
SELECT m.id, m.nombre, m.etapas, e.id AS stage, e.objetivos, o.id AS oid, o.tipo, o.args
FROM `{schema}`.`misiones` m
JOIN `{schema}`.`mision_etapas` e ON e.id=4000
LEFT JOIN `{schema}`.`mision_objetivos` o ON o.id=CAST(e.objetivos AS UNSIGNED)
WHERE m.id=701
LIMIT 5");

await DumpAsync("stage 4000 raw", $@"
SELECT id, nombre, objetivos, recompensas FROM `{schema}`.`mision_etapas` WHERE id IN (4000,4001,4020) LIMIT 5");

await DumpAsync("objs for stage 4000/4001 if numeric", $@"
SELECT id, tipo, args, detalle FROM `{schema}`.`mision_objetivos`
WHERE id IN (
  SELECT CAST(objetivos AS UNSIGNED) FROM `{schema}`.`mision_etapas` WHERE id IN (4000,4001) AND objetivos REGEXP '^[0-9]+$'
)
LIMIT 10");

await DumpAsync("preg* empty counts", $@"
SELECT
  SUM(pregDarMision IS NULL OR pregDarMision='') AS dar_empty,
  SUM(pregMisCompletada IS NULL OR pregMisCompletada='') AS comp_empty,
  SUM(pregMisIncompleta IS NULL OR pregMisIncompleta='') AS inc_empty,
  COUNT(*) AS total
FROM `{schema}`.`misiones`");

await DumpAsync("Same preg for comp and inc (sample)", $@"
SELECT COUNT(*) AS same_comp_inc FROM `{schema}`.`misiones`
WHERE pregMisCompletada<>'' AND pregMisCompletada=pregMisIncompleta");

await DumpAsync("MAX id < 1000 / gaps RUFUS", $@"
SELECT
  (SELECT MAX(id) FROM `{schema}`.`misiones` WHERE id < 1000) AS max_below_1k,
  (SELECT MAX(id) FROM `{schema}`.`misiones` WHERE id BETWEEN 1000 AND 9999) AS max_1k_9k,
  (SELECT COUNT(*) AS n FROM `{schema}`.`misiones` WHERE id BETWEEN 3500 AND 3999) AS count_3500s,
  (SELECT COUNT(*) AS n FROM `{schema}`.`misiones` WHERE id BETWEEN 2000 AND 2999) AS count_2000s");

Console.WriteLine("DONE3");
