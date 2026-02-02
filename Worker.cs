using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;

namespace CollectDataAudio
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly string _connectionString;
        private readonly List<LineConfig> _lines;
        private readonly string _baseFolderName;

        public Worker(ILogger<Worker> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("ProductionDB");
            _lines = configuration.GetSection("MonitorSettings:Lines").Get<List<LineConfig>>();
            _baseFolderName = configuration["MonitorSettings:BaseFolder"] ?? "Data Server";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var tasks = _lines.Select(line => ProcessLineAsync(line, stoppingToken));
                await Task.WhenAll(tasks);
                await Task.Delay(5000, stoppingToken);
            }
        }

        private async Task ProcessLineAsync(LineConfig line, CancellationToken token)
        {
            if (string.IsNullOrEmpty(line.Ip)) return;

            string sourcePath = $@"\\{line.Ip}\{_baseFolderName}";
            string processedPath = Path.Combine(sourcePath, "Processed");

            try
            {
                if (!Directory.Exists(sourcePath)) return;

                if (!Directory.Exists(processedPath)) Directory.CreateDirectory(processedPath);

                var files = Directory.GetFiles(sourcePath, "*.txt");

                foreach (var file in files)
                {
                    if (token.IsCancellationRequested) break;
                    await ProcessSingleFile(file, line.TableName, processedPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error Line {line.Name}: {ex.Message}");
            }
        }

        private async Task ProcessSingleFile(string filePath, string tableName, string processedPath)
        {
            string fileName = Path.GetFileName(filePath);
            string destFile = Path.Combine(processedPath, fileName);

            try
            {
                if (File.Exists(destFile))
                {
                    DateTime sourceTime = File.GetLastWriteTime(filePath);
                    DateTime destTime = File.GetLastWriteTime(destFile);

                    if (sourceTime <= destTime.AddSeconds(1)) return;

                    _logger.LogInformation($"File {fileName} terupdate. Memproses data baru...");
                }

                string fileNameNoExt = Path.GetFileNameWithoutExtension(filePath);
                DateTime fileDate;
                if (!DateTime.TryParseExact(fileNameNoExt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fileDate))
                {
                    fileDate = DateTime.Now;
                }

                var lines = await File.ReadAllLinesAsync(filePath);
                var dataToInsert = new List<ProductionData>();

                using (IDbConnection db = new SqlConnection(_connectionString))
                {
                    foreach (var lineData in lines.Skip(1))
                    {
                        if (string.IsNullOrWhiteSpace(lineData)) continue;

                        var parts = lineData.Split(',');

                        if (parts.Length < 6) continue;

                        string model = parts[0].Trim();
                        int target = TryParseInt(parts[1]);
                        int actual = TryParseInt(parts[2]);
                        // int ng = TryParseInt(parts[3]);
                        int dailyPlan = TryParseInt(parts[4]);
                        string serialNumber = parts[5].Trim();
                        int sut = TryParseInt(parts[6]);
                        string checkQuery = "";
                        object param = null;

                        if (!string.IsNullOrEmpty(serialNumber))
                        {
                            checkQuery = $"SELECT COUNT(1) FROM {tableName} WHERE SerialNumber = @SerialNumber";
                            param = new { SerialNumber = serialNumber };
                        }
                        else
                        {
                            checkQuery = $"SELECT COUNT(1) FROM {tableName} WHERE DateTime = @DateTime AND Model = @Model AND Target = @Target AND Actual = @Actual AND Sut = @Sut";
                            param = new { DateTime = fileDate, Model = model, Target = target, Actual = actual, Sut = sut };
                        }

                        int exists = await db.ExecuteScalarAsync<int>(checkQuery, param);

                        if (exists > 0) continue;

                        dataToInsert.Add(new ProductionData
                        {
                            DateTime = DateTime.Now,
                            Model = model,
                            Target = target,
                            Actual = actual,
                            DailyPlan = dailyPlan,
                            Weight = null,
                            Efficiency = null,
                            SerialNumber = serialNumber,
                            Sut = sut
                        });
                    }

                    if (dataToInsert.Count > 0)
                    {
                        string insertQuery = $@"
                    INSERT INTO {tableName} 
                    (DateTime, Model, DailyPlan, Target, Actual, Weight, Efficiency, SerialNumber, Sut)
                    VALUES 
                    (@DateTime, @Model, @DailyPlan, @Target, @Actual, @Weight, @Efficiency, @SerialNumber, @Sut)";

                        await db.ExecuteAsync(insertQuery, dataToInsert);
                        _logger.LogInformation($"Berhasil insert {dataToInsert.Count} data dari {fileName}");
                    }
                }

                File.Copy(filePath, destFile, true);
                File.SetLastWriteTime(destFile, File.GetLastWriteTime(filePath));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Gagal memproses {fileName}: {ex.Message}");
            }
        }

        // Helper function biar gak error kalau datanya kosong/bukan angka
        private int TryParseInt(string input)
        {
            if (int.TryParse(input, out int result)) return result;
            return 0;
        }
    }
}
