using System.Globalization;
using System.Text.RegularExpressions;

namespace RufusMapEditor.LegacyCompatibility.LangMaps;

/// <summary>
/// Parsea versions_es.txt buscando exclusivamente el token maps,es,N.
/// Soporta formato real: 11&amp;f=maps,es,1282|quests,es,... y lineas sueltas.
/// </summary>
public static class VersionsEsParser
{
    /// <summary>
    /// Token semantico maps,es,entero con frontera izquierda para evitar mymaps,es,N.
    /// </summary>
    private static readonly Regex MapsEsToken = new(
        @"(?<![A-Za-z0-9_])maps\s*,\s*es\s*,\s*(\d+)(?!\d)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>maps,es, seguido de valor no entero (p.ej. maps,es,abc).</summary>
    private static readonly Regex MapsEsMalformed = new(
        @"(?<![A-Za-z0-9_])maps\s*,\s*es\s*,\s*(?!\d)(\S+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParseMapsVersion(string versionsEsText, out int mapsVersion, out string? error)
    {
        mapsVersion = 0;
        error = null;
        var text = StripBom(versionsEsText);
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "versions_es.txt vacio.";
            return false;
        }

        var matches = MapsEsToken.Matches(text);
        if (matches.Count > 1)
        {
            error = "versions_es.txt contiene mas de un token maps,es,N (ambiguedad).";
            return false;
        }

        if (matches.Count == 1)
        {
            if (!int.TryParse(matches[0].Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out mapsVersion)
                || mapsVersion <= 0)
            {
                error = "Version maps,es invalida en versions_es.txt.";
                return false;
            }

            return true;
        }

        if (MapsEsMalformed.IsMatch(text))
        {
            error = "Version maps,es invalida en versions_es.txt.";
            return false;
        }

        error = "Token maps,es,N no encontrado en versions_es.txt.";
        return false;
    }

    public static string BuildSwfFileName(int mapsVersion) =>
        string.Create(CultureInfo.InvariantCulture, $"maps_es_{mapsVersion}.swf");

    private static readonly Regex DialogEsToken = new(
        @"(?<![A-Za-z0-9_])dialog\s*,\s*es\s*,\s*(\d+)(?!\d)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>CONT.6A/6B.2 — token activo dialog,es,N (independiente de maps,es).</summary>
    public static bool TryParseDialogVersion(string versionsEsText, out int dialogVersion, out string? error)
    {
        dialogVersion = 0;
        error = null;
        var text = StripBom(versionsEsText);
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "versions_es.txt vacío.";
            return false;
        }

        var matches = DialogEsToken.Matches(text);
        if (matches.Count > 1)
        {
            error = "versions_es.txt contiene más de un token dialog,es,N.";
            return false;
        }

        if (matches.Count == 0)
        {
            error = "Token dialog,es,N no encontrado en versions_es.txt.";
            return false;
        }

        if (!int.TryParse(matches[0].Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out dialogVersion)
            || dialogVersion <= 0)
        {
            error = "Versión dialog,es inválida en versions_es.txt.";
            return false;
        }

        return true;
    }

    public static string BuildDialogSwfFileName(int dialogVersion) =>
        string.Create(CultureInfo.InvariantCulture, $"dialog_es_{dialogVersion}.swf");

    public static string ExtractDialogLine(string versionsEsText)
    {
        if (!TryParseDialogVersion(versionsEsText, out var v, out _))
            return "";
        return string.Create(CultureInfo.InvariantCulture, $"dialog,es,{v}");
    }

    /// <summary>
    /// CONT.6C — sustituye únicamente dialog,es,{expectedCurrent} por dialog,es,{newVersion}.
    /// Conserva el resto del archivo (maps, quests, npc, etc.).
    /// </summary>
    public static bool TryBumpDialogVersion(
        string versionsEsText,
        int expectedCurrent,
        int newVersion,
        out string updatedText,
        out string? error)
    {
        updatedText = "";
        error = null;

        if (expectedCurrent <= 0 || newVersion <= 0)
        {
            error = "Versiones dialog,es inválidas para bump.";
            return false;
        }

        if (newVersion != expectedCurrent + 1)
        {
            error = $"Bump inválido: se espera N+1 ({expectedCurrent + 1}), recibido {newVersion}.";
            return false;
        }

        if (!TryParseDialogVersion(versionsEsText, out var current, out var parseError))
        {
            error = parseError;
            return false;
        }

        if (current != expectedCurrent)
        {
            error =
                $"Token dialog,es remoto es {current}, esperado {expectedCurrent} (snapshot obsoleto o cambio concurrente).";
            return false;
        }

        var text = StripBom(versionsEsText);
        var matches = DialogEsToken.Matches(text);
        if (matches.Count != 1)
        {
            error = "No se puede actualizar dialog,es: token ambiguo o ausente.";
            return false;
        }

        var m = matches[0];
        var replacement = string.Create(CultureInfo.InvariantCulture, $"dialog,es,{newVersion}");
        updatedText = text.Substring(0, m.Index) + replacement + text.Substring(m.Index + m.Length);

        if (!string.IsNullOrEmpty(versionsEsText) && versionsEsText[0] == '\uFEFF')
            updatedText = "\uFEFF" + updatedText;

        if (!TryParseDialogVersion(updatedText, out var verify, out var verifyErr) || verify != newVersion)
        {
            error = verifyErr ?? "Verificación post-bump dialog_es fallida.";
            updatedText = "";
            return false;
        }

        if (DialogEsToken.Matches(StripBom(updatedText)).Count != 1)
        {
            error = "Post-bump: ambigüedad dialog,es detectada.";
            updatedText = "";
            return false;
        }

        // maps,es must remain untouched if it existed.
        if (TryParseMapsVersion(versionsEsText, out var mapsBefore, out _)
            && TryParseMapsVersion(updatedText, out var mapsAfter, out _)
            && mapsBefore != mapsAfter)
        {
            error = "Post-bump: maps,es fue alterado (no permitido).";
            updatedText = "";
            return false;
        }

        return true;
    }

    private static readonly Regex NpcEsToken = new(
        @"(?<![A-Za-z0-9_])npc\s*,\s*es\s*,\s*(\d+)(?!\d)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>CONT.7A/7B — token activo npc,es,N.</summary>
    public static bool TryParseNpcVersion(string versionsEsText, out int npcVersion, out string? error)
    {
        npcVersion = 0;
        error = null;
        var text = StripBom(versionsEsText);
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "versions_es.txt vacío.";
            return false;
        }

        var matches = NpcEsToken.Matches(text);
        if (matches.Count > 1)
        {
            error = "versions_es.txt contiene más de un token npc,es,N.";
            return false;
        }

        if (matches.Count == 0)
        {
            error = "Token npc,es,N no encontrado en versions_es.txt.";
            return false;
        }

        if (!int.TryParse(matches[0].Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out npcVersion)
            || npcVersion <= 0)
        {
            error = "Versión npc,es inválida en versions_es.txt.";
            return false;
        }

        return true;
    }

    public static string BuildNpcSwfFileName(int npcVersion) =>
        string.Create(CultureInfo.InvariantCulture, $"npc_es_{npcVersion}.swf");

    /// <summary>
    /// CONT.7B — sustituye únicamente npc,es,{expectedCurrent} por npc,es,{newVersion}.
    /// Conserva maps, dialog, quests y el resto del archivo.
    /// </summary>
    public static bool TryBumpNpcVersion(
        string versionsEsText,
        int expectedCurrent,
        int newVersion,
        out string updatedText,
        out string? error)
    {
        updatedText = "";
        error = null;

        if (expectedCurrent <= 0 || newVersion <= 0)
        {
            error = "Versiones npc,es inválidas para bump.";
            return false;
        }

        if (newVersion != expectedCurrent + 1)
        {
            error = $"Bump inválido: se espera N+1 ({expectedCurrent + 1}), recibido {newVersion}.";
            return false;
        }

        if (!TryParseNpcVersion(versionsEsText, out var current, out var parseError))
        {
            error = parseError;
            return false;
        }

        if (current != expectedCurrent)
        {
            error =
                $"Token npc,es remoto es {current}, esperado {expectedCurrent} (snapshot obsoleto o cambio concurrente).";
            return false;
        }

        var text = StripBom(versionsEsText);
        var matches = NpcEsToken.Matches(text);
        if (matches.Count != 1)
        {
            error = "No se puede actualizar npc,es: token ambiguo o ausente.";
            return false;
        }

        var m = matches[0];
        var replacement = string.Create(CultureInfo.InvariantCulture, $"npc,es,{newVersion}");
        updatedText = text.Substring(0, m.Index) + replacement + text.Substring(m.Index + m.Length);

        if (!string.IsNullOrEmpty(versionsEsText) && versionsEsText[0] == '\uFEFF')
            updatedText = "\uFEFF" + updatedText;

        if (!TryParseNpcVersion(updatedText, out var verify, out var verifyErr) || verify != newVersion)
        {
            error = verifyErr ?? "Verificación post-bump npc_es fallida.";
            updatedText = "";
            return false;
        }

        if (NpcEsToken.Matches(StripBom(updatedText)).Count != 1)
        {
            error = "Post-bump: ambigüedad npc,es detectada.";
            updatedText = "";
            return false;
        }

        if (TryParseMapsVersion(versionsEsText, out var mapsBefore, out _)
            && TryParseMapsVersion(updatedText, out var mapsAfter, out _)
            && mapsBefore != mapsAfter)
        {
            error = "Post-bump: maps,es fue alterado (no permitido).";
            updatedText = "";
            return false;
        }

        if (TryParseDialogVersion(versionsEsText, out var dialogBefore, out _)
            && TryParseDialogVersion(updatedText, out var dialogAfter, out _)
            && dialogBefore != dialogAfter)
        {
            error = "Post-bump: dialog,es fue alterado (no permitido).";
            updatedText = "";
            return false;
        }

        return true;
    }

    /// <summary>
    /// CONT.8 — bump opcional de dialog,es y/o npc,es en una sola pasada.
    /// Pasa null en la capa que no cambia. Conserva maps/quests/resto.
    /// </summary>
    public static bool TryBumpContentClientVersions(
        string versionsEsText,
        int? dialogExpectedCurrent,
        int? dialogNewVersion,
        int? npcExpectedCurrent,
        int? npcNewVersion,
        out string updatedText,
        out string? error)
    {
        updatedText = "";
        error = null;

        var bumpDialog = dialogExpectedCurrent is int && dialogNewVersion is int;
        var bumpNpc = npcExpectedCurrent is int && npcNewVersion is int;
        if (!bumpDialog && !bumpNpc)
        {
            error = "Ningún token cliente a actualizar.";
            return false;
        }

        if (bumpDialog)
        {
            if (dialogNewVersion != dialogExpectedCurrent + 1)
            {
                error = $"Bump dialog inválido: se espera N+1 ({dialogExpectedCurrent + 1}), recibido {dialogNewVersion}.";
                return false;
            }

            if (!TryParseDialogVersion(versionsEsText, out var currentDialog, out var parseDialogErr))
            {
                error = parseDialogErr;
                return false;
            }

            if (currentDialog != dialogExpectedCurrent)
            {
                error =
                    $"Token dialog,es remoto es {currentDialog}, esperado {dialogExpectedCurrent}.";
                return false;
            }
        }

        if (bumpNpc)
        {
            if (npcNewVersion != npcExpectedCurrent + 1)
            {
                error = $"Bump npc inválido: se espera N+1 ({npcExpectedCurrent + 1}), recibido {npcNewVersion}.";
                return false;
            }

            if (!TryParseNpcVersion(versionsEsText, out var currentNpc, out var parseNpcErr))
            {
                error = parseNpcErr;
                return false;
            }

            if (currentNpc != npcExpectedCurrent)
            {
                error =
                    $"Token npc,es remoto es {currentNpc}, esperado {npcExpectedCurrent}.";
                return false;
            }
        }

        var text = StripBom(versionsEsText);
        if (bumpDialog)
        {
            var matches = DialogEsToken.Matches(text);
            if (matches.Count != 1)
            {
                error = "No se puede actualizar dialog,es: token ambiguo o ausente.";
                return false;
            }

            var m = matches[0];
            var replacement = string.Create(CultureInfo.InvariantCulture, $"dialog,es,{dialogNewVersion}");
            text = text.Substring(0, m.Index) + replacement + text.Substring(m.Index + m.Length);
        }

        if (bumpNpc)
        {
            var matches = NpcEsToken.Matches(text);
            if (matches.Count != 1)
            {
                error = "No se puede actualizar npc,es: token ambiguo o ausente.";
                return false;
            }

            var m = matches[0];
            var replacement = string.Create(CultureInfo.InvariantCulture, $"npc,es,{npcNewVersion}");
            text = text.Substring(0, m.Index) + replacement + text.Substring(m.Index + m.Length);
        }

        if (!string.IsNullOrEmpty(versionsEsText) && versionsEsText[0] == '\uFEFF')
            text = "\uFEFF" + text;

        if (bumpDialog)
        {
            if (!TryParseDialogVersion(text, out var verifyD, out var verifyDErr) || verifyD != dialogNewVersion)
            {
                error = verifyDErr ?? "Verificación post-bump dialog_es fallida.";
                return false;
            }
        }
        else if (TryParseDialogVersion(versionsEsText, out var keepD, out _)
                 && TryParseDialogVersion(text, out var afterD, out _)
                 && keepD != afterD)
        {
            error = "Post-bump: dialog,es fue alterado (no permitido).";
            return false;
        }

        if (bumpNpc)
        {
            if (!TryParseNpcVersion(text, out var verifyN, out var verifyNErr) || verifyN != npcNewVersion)
            {
                error = verifyNErr ?? "Verificación post-bump npc_es fallida.";
                return false;
            }
        }
        else if (TryParseNpcVersion(versionsEsText, out var keepN, out _)
                 && TryParseNpcVersion(text, out var afterN, out _)
                 && keepN != afterN)
        {
            error = "Post-bump: npc,es fue alterado (no permitido).";
            return false;
        }

        if (TryParseMapsVersion(versionsEsText, out var mapsBefore, out _)
            && TryParseMapsVersion(text, out var mapsAfter, out _)
            && mapsBefore != mapsAfter)
        {
            error = "Post-bump cliente: maps,es fue alterado (no permitido).";
            return false;
        }

        updatedText = text;
        return true;
    }

    /// <summary>Ruta efímera CONT.8 para sustitución de versions_es (no .bak permanente).</summary>
    public static string VersionsEsEphemeralPrevName => LangSftpSettings.VersionsFileName + ".rufus-prev";

    /// <summary>Devuelve el token canonico maps,es,N si existe exactamente uno.</summary>
    public static string ExtractMapsLine(string versionsEsText)
    {
        var text = StripBom(versionsEsText);
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var matches = MapsEsToken.Matches(text);
        if (matches.Count != 1)
            return "";

        if (!int.TryParse(matches[0].Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            || v <= 0)
            return "";

        return string.Create(CultureInfo.InvariantCulture, $"maps,es,{v}");
    }

    /// <summary>
    /// FASE 11B.2 — sustituye unicamente maps,es,{expectedCurrent} por maps,es,{newVersion}.
    /// Conserva el resto del archivo (quests, spells, etc.).
    /// </summary>
    public static bool TryBumpMapsVersion(
        string versionsEsText,
        int expectedCurrent,
        int newVersion,
        out string updatedText,
        out string? error)
    {
        updatedText = "";
        error = null;

        if (expectedCurrent <= 0 || newVersion <= 0)
        {
            error = "Versiones maps,es invalidas para bump.";
            return false;
        }

        if (newVersion != expectedCurrent + 1)
        {
            error = $"Bump invalido: se espera N+1 ({expectedCurrent + 1}), recibido {newVersion}.";
            return false;
        }

        if (!TryParseMapsVersion(versionsEsText, out var current, out var parseError))
        {
            error = parseError;
            return false;
        }

        if (current != expectedCurrent)
        {
            error =
                $"Token maps,es remoto es {current}, esperado {expectedCurrent} (snapshot obsoleto o cambio concurrente).";
            return false;
        }

        var text = StripBom(versionsEsText);
        var matches = MapsEsToken.Matches(text);
        if (matches.Count != 1)
        {
            error = "No se puede actualizar maps,es: token ambiguo o ausente.";
            return false;
        }

        var m = matches[0];
        var replacement = string.Create(CultureInfo.InvariantCulture, $"maps,es,{newVersion}");
        updatedText = text.Substring(0, m.Index) + replacement + text.Substring(m.Index + m.Length);

        // Preservar BOM si el original lo tenia.
        if (!string.IsNullOrEmpty(versionsEsText) && versionsEsText[0] == '\uFEFF')
            updatedText = "\uFEFF" + updatedText;

        if (!TryParseMapsVersion(updatedText, out var verify, out var verifyErr) || verify != newVersion)
        {
            error = verifyErr ?? "Verificacion post-bump fallida.";
            updatedText = "";
            return false;
        }

        // Garantizar que ningun otro maps,es aparecio.
        if (MapsEsToken.Matches(StripBom(updatedText)).Count != 1)
        {
            error = "Post-bump: ambiguedad maps,es detectada.";
            updatedText = "";
            return false;
        }

        return true;
    }

    private static readonly Regex MonstersEsToken = new(
        @"(?<![A-Za-z0-9_])monsters\s*,\s*es\s*,\s*(\d+)(?!\d)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ItemsEsToken = new(
        @"(?<![A-Za-z0-9_])items\s*,\s*es\s*,\s*(\d+)(?!\d)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>LIB.2 — token activo monsters,es,N desde versions_es.txt.</summary>
    public static bool TryParseMonstersVersion(string versionsEsText, out int monstersVersion, out string? error)
        => TryParseSingleToken(versionsEsText, MonstersEsToken, "monsters,es", out monstersVersion, out error);

    /// <summary>LIB.2 — token activo items,es,N desde versions_es.txt.</summary>
    public static bool TryParseItemsVersion(string versionsEsText, out int itemsVersion, out string? error)
        => TryParseSingleToken(versionsEsText, ItemsEsToken, "items,es", out itemsVersion, out error);

    public static string BuildMonstersSwfFileName(int monstersVersion) =>
        string.Create(CultureInfo.InvariantCulture, $"monsters_es_{monstersVersion}.swf");

    public static string BuildItemsSwfFileName(int itemsVersion) =>
        string.Create(CultureInfo.InvariantCulture, $"items_es_{itemsVersion}.swf");

    private static bool TryParseSingleToken(
        string versionsEsText,
        Regex token,
        string label,
        out int version,
        out string? error)
    {
        version = 0;
        error = null;
        var text = StripBom(versionsEsText);
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "versions_es.txt vacío.";
            return false;
        }

        var matches = token.Matches(text);
        if (matches.Count > 1)
        {
            error = $"versions_es.txt contiene más de un token {label},N.";
            return false;
        }

        if (matches.Count == 0)
        {
            error = $"Token {label},N no encontrado en versions_es.txt.";
            return false;
        }

        if (!int.TryParse(matches[0].Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out version)
            || version <= 0)
        {
            error = $"Versión {label} inválida en versions_es.txt.";
            return false;
        }

        return true;
    }

    private static string StripBom(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        return text[0] == '\uFEFF' ? text[1..] : text;
    }
}
