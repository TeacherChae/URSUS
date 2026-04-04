using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;

namespace URSUS.Export
{
    /// <summary>
    /// 로컬 HTTP 서버로 CSV 데이터를 제공한다.
    /// GET  /api/csv/download — 현재 데이터를 CSV로 다운로드
    /// POST /api/csv/download — JSON 요청 → CSV 변환 (미구현, 동일 CSV 반환)
    /// GET  /api/csv/status   — 서버 상태 확인
    /// </summary>
    public sealed class CsvEndpointServer : IDisposable
    {
        public const int DEFAULT_PORT = 58720;
        private const int MAX_PORT_RETRIES = 10;

        private HttpListener? _listener;
        private Thread? _listenerThread;
        private volatile bool _running;
        private string _csvData = string.Empty;
        private int _rowCount;
        private readonly object _lock = new();

        public bool IsRunning => _running;
        public int ActivePort { get; private set; } = -1;
        public string BaseUrl => $"http://localhost:{ActivePort}";

        /// <summary>
        /// 서버를 시작한다. 포트 충돌 시 다음 포트를 시도한다.
        /// </summary>
        /// <returns>바인딩된 실제 포트</returns>
        public int Start(int preferredPort)
        {
            if (_running) return ActivePort;

            for (int i = 0; i < MAX_PORT_RETRIES; i++)
            {
                int port = preferredPort + i;
                try
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add($"http://localhost:{port}/");
                    listener.Start();

                    _listener = listener;
                    ActivePort = port;
                    _running = true;

                    _listenerThread = new Thread(ListenLoop)
                    {
                        IsBackground = true,
                        Name = "URSUS-CsvEndpoint"
                    };
                    _listenerThread.Start();

                    return port;
                }
                catch (HttpListenerException)
                {
                    // 포트 사용 중 — 다음 포트 시도
                }
            }

            throw new InvalidOperationException(
                $"포트 {preferredPort}~{preferredPort + MAX_PORT_RETRIES - 1} 모두 사용 중입니다.");
        }

        /// <summary>
        /// CSV 데이터를 갱신한다.
        /// </summary>
        public void UpdateData(
            IReadOnlyList<string> legalCodes,
            IReadOnlyList<string> names,
            IReadOnlyList<double> areas,
            IReadOnlyList<double> values)
        {
            string csv = CsvExporter.Serialize(legalCodes, names, areas, values);
            lock (_lock)
            {
                _csvData = csv;
                _rowCount = legalCodes.Count;
            }
        }

        private void ListenLoop()
        {
            while (_running && _listener != null)
            {
                try
                {
                    var context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
                }
                catch (HttpListenerException)
                {
                    // 서버 중지 시 발생 — 정상
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? "";
                string method = context.Request.HttpMethod;

                if (path.Equals("/api/csv/download", StringComparison.OrdinalIgnoreCase) &&
                    (method == "GET" || method == "POST"))
                {
                    ServeCsv(context);
                }
                else if (path.Equals("/api/csv/status", StringComparison.OrdinalIgnoreCase) &&
                         method == "GET")
                {
                    ServeStatus(context);
                }
                else
                {
                    context.Response.StatusCode = 404;
                    WriteResponse(context, "text/plain", "Not Found");
                }
            }
            catch
            {
                try { context.Response.StatusCode = 500; context.Response.Close(); }
                catch { /* ignore */ }
            }
        }

        private void ServeCsv(HttpListenerContext context)
        {
            string csv;
            lock (_lock) { csv = _csvData; }

            if (string.IsNullOrEmpty(csv))
            {
                context.Response.StatusCode = 204;
                context.Response.Close();
                return;
            }

            context.Response.Headers.Add("Content-Disposition",
                "attachment; filename=\"URSUS_export.csv\"");
            WriteResponse(context, "text/csv; charset=utf-8", csv);
        }

        private void ServeStatus(HttpListenerContext context)
        {
            int rows;
            lock (_lock) { rows = _rowCount; }

            string json = $"{{\"status\":\"running\",\"port\":{ActivePort},\"rows\":{rows}}}";
            WriteResponse(context, "application/json", json);
        }

        private static void WriteResponse(HttpListenerContext context, string contentType, string body)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(body);
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.Close();
        }

        public void Dispose()
        {
            _running = false;
            try { _listener?.Stop(); } catch { /* ignore */ }
            try { _listener?.Close(); } catch { /* ignore */ }
            _listener = null;
            ActivePort = -1;
        }
    }
}
