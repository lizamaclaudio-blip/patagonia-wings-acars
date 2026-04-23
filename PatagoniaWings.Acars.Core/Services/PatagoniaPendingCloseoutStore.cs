using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    /// <summary>
    /// Cola local durable para no perder PIREPs cuando falla el submit remoto.
    /// Guarda el paquete final consolidado antes del envio.
    /// </summary>
    public sealed class PatagoniaPendingCloseoutStore
    {
        private readonly JavaScriptSerializer _serializer;
        private readonly string _folderPath;

        public PatagoniaPendingCloseoutStore(string folderPath = "")
        {
            _serializer = new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 256
            };

            _folderPath = string.IsNullOrWhiteSpace(folderPath)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PatagoniaWings",
                    "Acars",
                    "pending-pireps")
                : folderPath;

            Directory.CreateDirectory(_folderPath);
        }

        public string FolderPath => _folderPath;

        public void Save(PatagoniaPendingCloseoutEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException("envelope");

            if (string.IsNullOrWhiteSpace(envelope.Id))
            {
                envelope.Id = Guid.NewGuid().ToString("N");
            }

            if (envelope.CreatedAtUtc == default(DateTime))
            {
                envelope.CreatedAtUtc = DateTime.UtcNow;
            }

            var path = GetFilePath(envelope.Id);
            var json = _serializer.Serialize(envelope);
            File.WriteAllText(path, json);
        }

        public PatagoniaPendingCloseoutEnvelope? Load(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            var path = GetFilePath(id);
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return _serializer.Deserialize<PatagoniaPendingCloseoutEnvelope>(json);
        }

        public List<PatagoniaPendingCloseoutEnvelope> LoadAll()
        {
            var result = new List<PatagoniaPendingCloseoutEnvelope>();

            foreach (var file in Directory.GetFiles(_folderPath, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var envelope = _serializer.Deserialize<PatagoniaPendingCloseoutEnvelope>(json);
                    if (envelope != null)
                    {
                        result.Add(envelope);
                    }
                }
                catch
                {
                    // Se ignoran archivos corruptos para no frenar el resto de la cola.
                }
            }

            return result.OrderBy(item => item.CreatedAtUtc).ToList();
        }

        public int Count()
        {
            return Directory.Exists(_folderPath)
                ? Directory.GetFiles(_folderPath, "*.json").Length
                : 0;
        }

        public bool ExistsForReservation(string reservationId)
        {
            if (string.IsNullOrWhiteSpace(reservationId))
            {
                return false;
            }

            return LoadAll().Any(item =>
                string.Equals(item.ReservationId, reservationId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public void Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            var path = GetFilePath(id);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public void MarkAttempt(PatagoniaPendingCloseoutEnvelope envelope, string error = "")
        {
            if (envelope == null)
            {
                return;
            }

            envelope.RetryCount++;
            envelope.LastAttemptUtc = DateTime.UtcNow;
            envelope.LastError = error ?? string.Empty;
            Save(envelope);
        }

        private string GetFilePath(string id)
        {
            return Path.Combine(_folderPath, id.Trim() + ".json");
        }
    }
}
