using System;
using System.IO;

namespace KassenbuchApp
{
    public static class AppConfig
    {
        public static string DbFilePath
        {
            get
            {
                // DB liegt neben der EXE
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kassenbuch.db");
            }
        }

        public static string ConnectionString =>
            $"Data Source={DbFilePath};Version=3;foreign keys=true;";
    }
}
