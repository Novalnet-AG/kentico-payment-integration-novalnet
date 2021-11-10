using CMS.DataEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CMS.EventLog;
using CMS.Helpers;
using System.Security.Cryptography;
using System.IO;
using CMS.SiteProvider;
using System.Web;
using System.Net;
using System.Collections.Specialized;
namespace NovalnetPayment
{
    public class NovalnetHelper
    {        

        /// <summary>
        /// Get Orderculture
        /// </summary>
        /// <param name="orderCulture"></param>
        /// <returns></returns>
        public static string GetLanguage(string orderCulture)
        {
            string[] language = orderCulture.Split('-');
            string culture = language[0].ToUpper();
            return culture;
        }
         
        /// <summary>
        /// Get payment key
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static string GetPaymentKey(NameValueCollection data)
        {
            string paymentKey = "";
            if (data["key"] == null)
            {
                paymentKey = data["payment_id"];
            }
            else
            {
                paymentKey = data["key"];
            }
            return paymentKey;
        }

        /// <summary>
        /// Get Novalnet Payment method
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static string GetPaymentMethod(NameValueCollection data)
        {
            Dictionary<string, string> paymentMethod = new Dictionary<string, string>();
            paymentMethod.Add("invoice", ResHelper.GetString("custom.novalnet_invoice"));
            paymentMethod.Add("prepayment", ResHelper.GetString("custom.novalnet_prepayment"));
            paymentMethod.Add("37", ResHelper.GetString("custom.novalnet_sepa"));
            paymentMethod.Add("6", ResHelper.GetString("custom.novalnet_creditcard"));
            paymentMethod.Add("33", ResHelper.GetString("custom.novalnet_sofort"));
            paymentMethod.Add("49", ResHelper.GetString("custom.novalnet_ideal"));
            paymentMethod.Add("50", ResHelper.GetString("custom.novalnet_eps"));
            paymentMethod.Add("69", ResHelper.GetString("custom.novalnet_giropay"));
            paymentMethod.Add("59", ResHelper.GetString("custom.novalnet_barzahlen"));
            paymentMethod.Add("34", ResHelper.GetString("custom.novalnet_paypal"));
            paymentMethod.Add("78", ResHelper.GetString("custom.novalnet_przelewy24"));            
            string paymentKey = GetPaymentKey(data) == "27" ? data["invoice_type"].ToLower() : GetPaymentKey(data) == "41" ? "invoice" : GetPaymentKey(data) == "40" ? "37" : GetPaymentKey(data) ;
            string NovalnetpaymentMethod = null;
            if (paymentKey != null && paymentMethod.ContainsKey(paymentKey))
            {
                NovalnetpaymentMethod = paymentMethod[paymentKey];
            }
            return NovalnetpaymentMethod;
        }
        /// <summary>
        /// Writes a error message to the event log.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="eventCode"></param>
        public static void LogEventError(string message, string eventCode)
        {
            EventLogProvider.LogEvent(EventType.ERROR, "Novalnet Payment Execution", eventCode, message);
        }

        /// <summary>
        /// Writes an exception to the event log.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="eventCode"></param>
        public static void LogEventException(Exception message, string eventCode)
        {
            EventLogProvider.LogException("Novalnet Payment Execution", eventCode, message, SiteContext.CurrentSiteID);
        }

        /// <summary>
        /// Writes a payment execution information to the eventlog.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="eventCode"></param>
        public static void LogEventInfo(string message, string eventCode)
        {
            EventLogProvider.LogEvent(EventType.INFORMATION, "Novalnet Payment Execution", eventCode, message);
        }

        /// <summary>
        /// Send Novalnet payport request
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        public static Dictionary<string, string> SendRequest(string param)
        {
            try
            {
                string postData = param.ToString();
                byte[] byteArray = Encoding.UTF8.GetBytes(postData);
                var gateway_timeout = SettingsKeyInfoProvider.GetValue("gateway_timeout", SiteContext.CurrentSiteID);
                WebRequest request = (HttpWebRequest)WebRequest.Create("https://paygate.novalnet.de/paygate.jsp");
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";
                request.ContentLength = byteArray.Length;
                request.Timeout = (!string.IsNullOrEmpty(gateway_timeout) ? Convert.ToInt32(gateway_timeout) : 240) * 1000;
                Stream dataStream = request.GetRequestStream();
                dataStream.Write(byteArray, 0, byteArray.Length);
                dataStream.Close();
                WebResponse response = (HttpWebResponse)request.GetResponse();
                dataStream = response.GetResponseStream();
                StreamReader reader = new StreamReader(dataStream);
                string responseFromServer = reader.ReadToEnd();
                reader.Close();
                dataStream.Close();
                response.Close();
                var responseVal = responseFromServer.Split('&').Select(x => x.Split('=')).ToDictionary(x => x[0], x => x[1]);
                return responseVal;
            }
            catch (Exception ex)
            {
                LogEventException(ex, "Error");
                return null;
            }

        }       

        /// <summary>
        /// Generate unique id with 16 digit unique number to use in the encryption of parameters.
        /// </summary>
        public static string GetUniqueid()
        {
            string[] randomStr = new string[] { "8", "7", "6", "5", "4", "3", "2", "1", "9", "0", "9", "7", "6", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
            Random rnd = new Random();
            string[] randomArr = randomStr.OrderBy(x => rnd.Next()).ToArray();
            string uniqueVal = string.Join("", randomArr);
            return uniqueVal.Substring(0, 16);
        }

        /// <summary>
        /// Generate encrypted value based on AES-256
        /// </summary>
        public static string EncryptString(string data, string password, string uniqid)
        {
            try
            {
                // Instantiate a new Aes object to perform string symmetric encryption
                Aes encryptor = Aes.Create();

                //encryptor.KeySize = 256;
                //encryptor.BlockSize = 128;
                //encryptor.Padding = PaddingMode.Zeros;
                encryptor.Mode = CipherMode.CBC;

                // Set key and IV
                encryptor.Key = Encoding.ASCII.GetBytes(password);
                encryptor.IV = Encoding.ASCII.GetBytes(uniqid);

                // Instantiate a new MemoryStream object to contain the encrypted bytes
                MemoryStream memoryStream = new MemoryStream();

                // Instantiate a new encryptor from our Aes object
                ICryptoTransform aesEncryptor = encryptor.CreateEncryptor();

                // Instantiate a new CryptoStream object to process the data and write it to the
                // memory stream
                CryptoStream cryptoStream = new CryptoStream(memoryStream, aesEncryptor, CryptoStreamMode.Write);

                // Convert the data string into a byte array
                byte[] plainBytes = Encoding.ASCII.GetBytes(data);

                // Encrypt the input plaintext string
                cryptoStream.Write(plainBytes, 0, plainBytes.Length);

                // Complete the encryption process
                cryptoStream.FlushFinalBlock();

                // Convert the encrypted data from a MemoryStream to a byte array
                byte[] cipherBytes = memoryStream.ToArray();

                // Close both the MemoryStream and the CryptoStream
                memoryStream.Close();
                cryptoStream.Close();

                // Convert the encrypted byte array to a base64 encoded string
                string cipherText = Convert.ToBase64String(cipherBytes, 0, cipherBytes.Length);
                return cipherText;
            }
            catch (Exception ex)
            {
                LogEventException(ex, "Encryption");
                return null;
            }
            // Return the encrypted data as a string

        }

        /// <summary>
        /// Generate hash value
        /// </summary>
        public static string GetHash(Dictionary<string, string> data, string uniqid, string password)
        {
            try
            {
                SHA256 sha256 = SHA256Managed.Create();
                byte[] bytes = Encoding.UTF8.GetBytes(data["authCode"] + data["product"] + data["tariff"] + data["amount"] + data["testMode"] + uniqid + Reverse(password));
                byte[] hash = sha256.ComputeHash(bytes);
                return GetStringFromHash(hash);
            }
            catch (Exception ex)
            {
                LogEventException(ex, "Encryption");
                return null;
            }
        }

        /// <summary>
        /// Get string from hash value
        /// </summary>
        public static string GetStringFromHash(byte[] hash)
        {
            StringBuilder result = new StringBuilder();
            try
            {
                for (int i = 0; i < hash.Length; i++)
                {
                    result.Append(hash[i].ToString("X2"));
                }
            }
            catch (Exception ex)
            {
                LogEventException(ex, "Encryption");
            }
            return result.ToString().ToLower();
        }

        /// <summary>
        /// Get reverse string
        /// </summary>
        public static string Reverse(string password)
        {
            char[] charArray = password.ToCharArray();
            Array.Reverse(charArray);
            return new string(charArray);
        }

        /// <summary>
        /// Get decrypted string value
        /// </summary>
        public static string DecryptString(string data, string password, string uniqid)
        {
            // Instantiate a new Aes object to perform string symmetric encryption
            Aes encryptor = Aes.Create();

            encryptor.Mode = CipherMode.CBC;
            //encryptor.KeySize = 256;
            //encryptor.BlockSize = 128;
            //encryptor.Padding = PaddingMode.Zeros;

            // Set key and IV
            encryptor.Key = Encoding.ASCII.GetBytes(password);
            encryptor.IV = Encoding.ASCII.GetBytes(uniqid);

            // Instantiate a new MemoryStream object to contain the encrypted bytes
            MemoryStream memoryStream = new MemoryStream();

            // Instantiate a new encryptor from our Aes object
            ICryptoTransform aesDecryptor = encryptor.CreateDecryptor();

            // Instantiate a new CryptoStream object to process the data and write it to the 
            // memory stream
            CryptoStream cryptoStream = new CryptoStream(memoryStream, aesDecryptor, CryptoStreamMode.Write);

            // Will contain decrypted plaintext
            string plainText = String.Empty;

            try
            {
                // Convert the ciphertext string into a byte array
                byte[] cipherBytes = Convert.FromBase64String(data);

                // Decrypt the input ciphertext string
                cryptoStream.Write(cipherBytes, 0, cipherBytes.Length);

                // Complete the decryption process
                cryptoStream.FlushFinalBlock();

                // Convert the decrypted data from a MemoryStream to a byte array
                byte[] plainBytes = memoryStream.ToArray();

                // Convert the decrypted byte array to string
                plainText = Encoding.ASCII.GetString(plainBytes, 0, plainBytes.Length);
            }
            catch (Exception ex)
            {
                LogEventException(ex, "Decryption");
            }
            finally
            {
                // Close both the MemoryStream and the CryptoStream
                memoryStream.Close();
                cryptoStream.Close();
            }

            // Return the decrypted data as a string
            return plainText;
        }

        /// <summary>
        /// Get session value from payment page url for showing error message 
        /// </summary>
        /// <returns></returns>
        public static string GetSessionValue()
        {
            var urlQueryString = HttpContext.Current.Request.QueryString;
            string sessionVal = "";
            if (urlQueryString.AllKeys.Contains("o"))
            {
                sessionVal = urlQueryString["o"];
            }
            return sessionVal;
        }

        /// <summary>
        /// Get vendor credentials from backend payment configuration
        /// </summary>
        /// <returns></returns>
        public static Dictionary<string, string> GetVendorCredentials()
        {
            Dictionary<string, string> credentails = new Dictionary<string, string>();
            credentails.Add("vendor", SettingsKeyInfoProvider.GetValue("vendor_id", SiteContext.CurrentSiteID));
            credentails.Add("authCode", SettingsKeyInfoProvider.GetValue("auth_code", SiteContext.CurrentSiteID));
            credentails.Add("product", SettingsKeyInfoProvider.GetValue("product_id", SiteContext.CurrentSiteID));
            credentails.Add("tariff", SettingsKeyInfoProvider.GetValue("tariff_id", SiteContext.CurrentSiteID));
            credentails.Add("password", SettingsKeyInfoProvider.GetValue("payment_access_key", SiteContext.CurrentSiteID));
            return credentails;
        }

        /// <summary>
        /// Get order total amount
        /// </summary>
        /// <param name="OrderAmount"></param>
        /// <returns></returns>
        public static int GetOrderAmount(decimal OrderAmount)
        {
            decimal orderGrandTotal = OrderAmount * 100;
            int amount = (int)orderGrandTotal;
            return amount;
        }
    }
}
