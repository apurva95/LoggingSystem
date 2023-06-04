using LoggerLibrary;
using Microsoft.AspNetCore.Http;
using System.Text;

namespace LoggerTest
{
    public class HttpContextSessionIdProvider : ISessionIdProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public HttpContextSessionIdProvider(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string? GetSessionId()
        {
            var httpContext = _httpContextAccessor.HttpContext;

            if (httpContext != null && httpContext.Session != null)
            {
                string sessionId = httpContext.Session.GetString("sessionId");
                if (sessionId == null)
                {
                    sessionId = Guid.NewGuid().ToString();
                    httpContext.Session.SetString("sessionId", sessionId);
                }

                return sessionId;
            }
            return null;
        }
    }

    public static class SessionExtensions
    {
        /// <summary>
        /// Sets an int value in the <see cref="ISession"/>.
        /// </summary>
        /// <param name="session">The <see cref="ISession"/>.</param>
        /// <param name="key">The key to assign.</param>
        /// <param name="value">The value to assign.</param>
        public static void SetInt32(this ISession session, string key, int value)
        {
            var bytes = new byte[]
            {
                (byte)(value >> 24),
                (byte)(0xFF & (value >> 16)),
                (byte)(0xFF & (value >> 8)),
                (byte)(0xFF & value)
            };
            session.Set(key, bytes);
        }

        /// <summary>
        /// Gets an int value from <see cref="ISession"/>.
        /// </summary>
        /// <param name="session">The <see cref="ISession"/>.</param>
        /// <param name="key">The key to read.</param>
        public static int? GetInt32(this ISession session, string key)
        {
            var data = session.Get(key);
            if (data == null || data.Length < 4)
            {
                return null;
            }
            return data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3];
        }

        /// <summary>
        /// Sets a <see cref="string"/> value in the <see cref="ISession"/>.
        /// </summary>
        /// <param name="session">The <see cref="ISession"/>.</param>
        /// <param name="key">The key to assign.</param>
        /// <param name="value">The value to assign.</param>
        public static void SetString(this ISession session, string key, string value)
        {
            session.Set(key, Encoding.UTF8.GetBytes(value));
        }

        /// <summary>
        /// Gets a string value from <see cref="ISession"/>.
        /// </summary>
        /// <param name="session">The <see cref="ISession"/>.</param>
        /// <param name="key">The key to read.</param>
        public static string? GetString(this ISession session, string key)
        {
            var data = session.Get(key);
            if (data == null)
            {
                return null;
            }
            return Encoding.UTF8.GetString(data);
        }

        /// <summary>
        /// Gets a byte-array value from <see cref="ISession"/>.
        /// </summary>
        /// <param name="session">The <see cref="ISession"/>.</param>
        /// <param name="key">The key to read.</param>
        public static byte[]? Get(this ISession session, string key)
        {
            session.TryGetValue(key, out var value);
            return value;
        }
    }
}
