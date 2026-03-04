using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using CertWatchdog.Models;

namespace CertWatchdog.Services
{
    internal static class CertificateCheckService
    {
        public static CertificateInfo CheckCertificate(string httpsUrl, string serviceType = "")
        {
            var info = new CertificateInfo
            {
                Url = httpsUrl,
                Endpoint = GetEndpointName(httpsUrl),
                ServiceType = serviceType,
                LastChecked = DateTime.UtcNow
            };

            try
            {
                var request = (HttpWebRequest)WebRequest.Create(httpsUrl);
                request.Method = "HEAD";
                request.Timeout = 15000;
                request.AllowAutoRedirect = false;

                X509Certificate2 cert = null;

                request.ServerCertificateValidationCallback = (object sender, X509Certificate certificate,
                    X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
                {
                    if (certificate != null)
                    {
                        cert = new X509Certificate2(certificate);
                    }
                    // Always return true - we are inspecting, not enforcing trust
                    return true;
                };

                try
                {
                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        // Response received, cert should be captured
                    }
                }
                catch (WebException ex) when (ex.Response != null)
                {
                    // HTTP error responses still give us the cert
                    ex.Response.Dispose();
                }

                try
                {
                    if (cert != null)
                    {
                        info.Issuer = cert.Issuer;
                        info.Subject = cert.Subject;
                        info.NotAfter = cert.NotAfter;
                        info.DaysLeft = (int)(cert.NotAfter - DateTime.UtcNow).TotalDays;
                        info.Status = CertificateInfo.ClassifyDaysLeft(info.DaysLeft);
                    }
                    else
                    {
                        info.Status = CertStatus.Error;
                        info.ErrorMessage = "No certificate received";
                    }
                }
                finally
                {
                    cert?.Dispose();
                }
            }
            catch (Exception ex)
            {
                info.Status = CertStatus.Error;
                info.ErrorMessage = ex.Message;
            }

            return info;
        }

        public static List<CertificateInfo> CheckAllCertificates(List<EndpointInfo> endpoints)
        {
            var results = new ConcurrentBag<CertificateInfo>();

            Parallel.ForEach(endpoints,
                new ParallelOptions { MaxDegreeOfParallelism = 40 },
                endpoint =>
                {
                    try
                    {
                        var certInfo = CheckCertificate(endpoint.Url, endpoint.ServiceType);
                        certInfo.SourceItemId = endpoint.SourceItemId;
                        results.Add(certInfo);
                    }
                    catch (Exception ex)
                    {
                        results.Add(new CertificateInfo
                        {
                            Url = endpoint.Url,
                            Endpoint = GetEndpointName(endpoint.Url),
                            ServiceType = endpoint.ServiceType,
                            SourceItemId = endpoint.SourceItemId,
                            Status = CertStatus.Error,
                            ErrorMessage = ex.Message,
                            LastChecked = DateTime.UtcNow
                        });
                    }
                });

            return results.ToList();
        }

        private static string GetEndpointName(string url)
        {
            try
            {
                var uri = new Uri(url);
                return uri.Host;
            }
            catch
            {
                return url;
            }
        }
    }
}
