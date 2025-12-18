namespace DBScriptBackup

{
    public class BackupConfiguration
    {
        public string ServerName { get; set; }
        public string BasePath { get; set; }
        public DatabaseConfig[] Databases { get; set; }
    }
}