namespace RufusMapEditor.LegacyCompatibility.LangMaps;

/// <summary>
/// FASE 11B.2 — superficie SFTP de publicacion LANG (escritura acotada).
/// Extiende lectura. No expone Delete generico ni sobrescritura de SWF existentes.
/// </summary>
public interface ILangSftpPublishClient : ILangSftpReadClient
{
    /// <summary>Sube un archivo nuevo. Falla si la ruta remota ya existe. Nunca sobrescribe.</summary>
    void UploadNewFile(string remotePath, byte[] content);

    /// <summary>
    /// Sustitución segura de versions_es (u otro archivo de texto):
    /// sube temp → renombra actual a prev efímero → renombra temp a final → elimina prev.
    /// CONT.8: no deja <c>.bak.*</c> permanentes. No incrementa <see cref="DeleteAttemptCount"/>.
    /// <paramref name="backupRemotePath"/> debe ser una ruta efímera (p.ej. <c>versions_es.txt.rufus-prev</c>).
    /// </summary>
    void ReplaceFileAtomically(string remotePath, byte[] content, string backupRemotePath);

    /// <summary>Debe permanecer en 0 — no se borran SWF ni archivos de contenido.</summary>
    int DeleteAttemptCount { get; }
}

public static class LangSftpPublishClientFactory
{
    public static ILangSftpPublishClient Create(LangSftpSettings settings, string plainPassword) =>
        new SshNetLangSftpPublishClient(settings, plainPassword);
}
