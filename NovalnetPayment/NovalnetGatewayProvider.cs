using System;
using System.Web;
using System.Collections.Generic;
using CMS.Ecommerce;
using System.Web.UI;
using CMS.DataEngine;
using System.Linq;
using System.Net;
using System.Text;
using System.IO;
using System.Security.Cryptography;
using CMS.Helpers;
using CMS.SiteProvider;
using System.Net.Http;

public class NovalnetGatewayProvider : CMSPaymentGatewayProvider, IDirectPaymentGatewayProvider
{
    /// <summary>
    /// Validates data submitted by the customer through the related payment form.    
    /// </summary>
    public override string ValidateCustomData(IDictionary<string, object> paymentData)
    {
        string vendor = SettingsKeyInfoProvider.GetValue("vendor_id", SiteContext.CurrentSiteID);
        string authCode = SettingsKeyInfoProvider.GetValue("auth_code", SiteContext.CurrentSiteID);
        string product = SettingsKeyInfoProvider.GetValue("product_id", SiteContext.CurrentSiteID);
        string tariff = SettingsKeyInfoProvider.GetValue("tariff_id", SiteContext.CurrentSiteID);
        string password = SettingsKeyInfoProvider.GetValue("payment_access_key", SiteContext.CurrentSiteID);
        if (string.IsNullOrEmpty(vendor) || string.IsNullOrEmpty(authCode) || string.IsNullOrEmpty(product) || string.IsNullOrEmpty(tariff) || string.IsNullOrEmpty(password))
        {
         
            return ResHelper.GetString("custom.basic_param_validation");
        }
      
        return string.Empty;
    }
    


    /// <summary>
    /// Processes the payment after a customer submits valid data through the payment form.
    /// </summary>
    public PaymentResultInfo ProcessPayment(IDictionary<string, object> paymentData)
    {
        // Build Novalnet payport request
        
        // Get unique id (16 digit unique number)
        string uniqid = getUniqueid();
        var addressInfo = OrderAddressInfoProvider.GetAddressInfo(Order.OrderID);
        // Encrypted vendor parameters               
        string vendor = SettingsKeyInfoProvider.GetValue("vendor_id", SiteContext.CurrentSiteID);
        string password = SettingsKeyInfoProvider.GetValue("payment_access_key", SiteContext.CurrentSiteID);
        string authCode = EncryptString(SettingsKeyInfoProvider.GetValue("auth_code", SiteContext.CurrentSiteID), password, uniqid);
        string product = EncryptString(SettingsKeyInfoProvider.GetValue("product_id", SiteContext.CurrentSiteID), password, uniqid);
        string tariff = EncryptString(SettingsKeyInfoProvider.GetValue("tariff_id", SiteContext.CurrentSiteID), password, uniqid);
        string testMode = EncryptString(((SettingsKeyInfoProvider.GetValue("test_mode", SiteContext.CurrentSiteID) == "True") ? "1" : "0"), password, uniqid);
        
        // Encrypted order amount
        decimal orderGrandTotal = Order.OrderGrandTotal * 100;
        int amount = Convert.ToInt32(orderGrandTotal);
        string encryptedAmount = EncryptString(Convert.ToString(amount), password, uniqid);
        // Generate Hash value using encrypted vendor parameters
        string hashVal = getHash(authCode, product, tariff, encryptedAmount, testMode, uniqid, password);
        // Additional vendor reference parameters
        string referrerId = SettingsKeyInfoProvider.GetValue("referrer_id", SiteContext.CurrentSiteID);
        // Get the amount for on-hold process
        string onholdAmount = SettingsKeyInfoProvider.GetValue("on_hold_limit", SiteContext.CurrentSiteID);

        // Shop configuration parameters
        int country = addressInfo.AddressCountryID;        
        CMS.Globalization.CountryInfo entity = CMS.Globalization.CountryInfoProvider.GetCountryInfo(country);
        string countryCode = entity.CountryTwoLetterCode;
        string lang = Order.OrderCulture;
        string[] language = lang.Split('-');
        string culture = language[0].ToUpper();
        var urlBuilder = new System.UriBuilder(HttpContext.Current.Request.Url.AbsoluteUri)
        {
            Path = (HttpContext.Current.Handler as Page).ResolveUrl("~/NovalnetResponseHandler.ashx"),
            Query = null,
        };
        Uri uri = urlBuilder.Uri;
        string url = urlBuilder.ToString();
        string currentUrl = HttpContext.Current.Request.Url.AbsoluteUri.ToString();

        string[] parts = currentUrl.Split(new char[] { '?', '&' });
        string sessionVal = parts[1].Replace("o=", "");

        // Payment configuration parameters
        // Credit Card
        bool cc3d = Convert.ToBoolean(SettingsKeyInfoProvider.GetValue("cc3d", SiteContext.CurrentSiteID));
        // Invoice 
        string invoiceDuedate = SettingsKeyInfoProvider.GetValue("invoice_due_date", SiteContext.CurrentSiteID);
        // Barzahlen
        string cashPaymentDuedate = SettingsKeyInfoProvider.GetValue("slip_expiry_date", SiteContext.CurrentSiteID);
        //Direct Debit SEPA
        string sepaDuedate = SettingsKeyInfoProvider.GetValue("sepa_due_date", SiteContext.CurrentSiteID);
        
        var param = new StringBuilder();

        param.AppendFormat("&vendor={0}", vendor);
        param.AppendFormat("&auth_code={0}", authCode);
        param.AppendFormat("&product={0}", product);
        param.AppendFormat("&tariff={0}", tariff);
        param.AppendFormat("&test_mode={0}", testMode);
        param.AppendFormat("&hash={0}", hashVal);
        param.AppendFormat("&uniqid={0}", uniqid);
        param.AppendFormat("&implementation={0}", "ENC");
        param.AppendFormat("&return_url={0}", url);
        param.AppendFormat("&error_return_url={0}", url);
        param.AppendFormat("&input1={0}", "reference1");
        param.AppendFormat("&inputval1={0}", sessionVal);
        param.AppendFormat("&hfooter={0}", "1");
        param.AppendFormat("&skip_cfm={0}", "1");
        param.AppendFormat("&skip_suc={0}", "1");
        param.AppendFormat("&thide={0}", "1");        
        param.AppendFormat("&purl={0}", "1");
        param.AppendFormat("&country_code={0}", countryCode);
        param.AppendFormat("&lang={0}", culture);
        param.AppendFormat("&first_name={0}", ShoppingCartInfoObj.Customer.CustomerFirstName);
        param.AppendFormat("&last_name={0}", ShoppingCartInfoObj.Customer.CustomerLastName);
        param.AppendFormat("&gender={0}", "u");
        param.AppendFormat("&order_no={0}", OrderId.ToString());
        param.AppendFormat("&amount={0}", encryptedAmount);
        param.AppendFormat("&currency={0}", CurrencyInfoProvider.GetCurrencyInfo(Order.OrderCurrencyID).CurrencyCode);
        param.AppendFormat("&email={0}", ShoppingCartInfoObj.Customer.CustomerEmail);
        param.AppendFormat("&city={0}", addressInfo.AddressCity);
        param.AppendFormat("&customer_no={0}", mOrder.OrderCustomerID.ToString());
        param.AppendFormat("&zip={0}", addressInfo.AddressZip);                
        
        if (!string.IsNullOrEmpty(addressInfo.AddressLine1) 
            && !string.IsNullOrEmpty(addressInfo.AddressLine2))
        {
            param.AppendFormat("&house_no={0}", addressInfo.AddressLine1);
            param.AppendFormat("&street={0}", addressInfo.AddressLine2);
        }
        else
        {
            param.AppendFormat("&street={0}", addressInfo.AddressLine1);
            param.AppendFormat("&search_in_street={0}", "1");

        }

        if (!string.IsNullOrEmpty(referrerId))
        {
            param.AppendFormat("&referrer_id={0}", referrerId);
        }

        if ((!string.IsNullOrEmpty(onholdAmount)) && (amount >= Convert.ToInt64(onholdAmount)))
        {
            param.AppendFormat("&on_hold={0}", "1");                              
        }
            
        if (cc3d)
        {
            param.AppendFormat("&cc_3d={0}", "1");
        }        
        
        if (!string.IsNullOrEmpty(invoiceDuedate))
        {            
            param.AppendFormat("&due_date={0}", invoiceDuedate);
        }
        if (!string.IsNullOrEmpty(sepaDuedate))
        {            
            param.AppendFormat("&due_date={0}", sepaDuedate);
        }
        
        if (!string.IsNullOrEmpty(cashPaymentDuedate))
        {
            var now = DateTime.Now;
            var due_date = now.AddDays(Convert.ToInt32(cashPaymentDuedate)).ToString("yyyy-MM-dd");
            param.AppendFormat("&cashpayment_due_date={0}", due_date);
        }

        string postData = param.ToString();
        byte[] byteArray = Encoding.UTF8.GetBytes(postData);
        var gateway_timeout = SettingsKeyInfoProvider.GetValue("gateway_timeout", SiteContext.CurrentSiteID);        
        
        WebRequest request = (HttpWebRequest)WebRequest.Create("https://paygate.novalnet.de/paygate.jsp");       

        ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
        request.Method = "POST";
        request.ContentType = "application/x-www-form-urlencoded";
        request.ContentLength = byteArray.Length;        
        request.Timeout = (!string.IsNullOrEmpty(gateway_timeout) ? Convert.ToInt32(gateway_timeout) : 240)  * 1000;
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
        // Sets the external gateway URL into the payment results
        // Customers are redirected to this URL to finish the payment after they successfully submit the payment form
        if ((Convert.ToInt32(responseVal["status"]) != 100) || string.IsNullOrEmpty(responseVal["url"]))
        {
            PaymentResult.PaymentIsFailed = true;
            ErrorMessage = responseVal["status_desc"];
            PaymentResult.PaymentDescription = responseVal["status_desc"];
            UpdateOrderPaymentResult();
            return PaymentResult;

        }
        PaymentResult.PaymentApprovalUrl = responseVal["url"];

        // Saves the payment result to the related order
        // Automatically sets the PaymentResult time stamp (date) and payment method properties        
        UpdateOrderPaymentResult();

        // Returns the partially set payment results.
        // The customer finishes the payment on an external page, so the example assumes that a HTTP handler (IPN)
        // later processes the gateway's reply, and calls the provider's 'ProcessDirectPaymentReply' method.        
        return PaymentResult;
    }
          
    /// <summary>
    /// Processes the payment results based on a reply from the payment gateway.
    /// For example, the method can be called from a custom IPN handler that accepts responses/notifications from
    /// the external gateway's responses/notifications.
    /// </summary>
    public void ProcessDirectPaymentReply(bool success, HttpContext context, string testMode)
    {
        // Payport response data
        var resultData = context.Request.Form;
        // Set payment transaction ID
        PaymentResult.PaymentTransactionID = resultData["tid"];
        // Set transaction mode (Test/Live)
        PaymentResultItemInfo customItem = new PaymentResultItemInfo();
        customItem.Header = ResHelper.GetString("custom.transaction_mode") + ": ";
        customItem.Name = "PaymentMode";
        customItem.Value = (testMode == "1") ? ResHelper.GetString("custom.test_mode") : ResHelper.GetString("custom.live_mode");
        PaymentResult.SetPaymentResultItemInfo(customItem);
        PaymentResult.PaymentMethodName = resultData["payment_type"];
        // Handle payment response
        string payment_key = "";
        if (resultData["key"] == null)
        {
            payment_key = resultData["payment_id"];
        }
        else
        {
            payment_key = resultData["key"];
        }
        if (success)
        {
            int[] onHoldStatus = new int[] { 75, 85, 86, 90, 91, 98, 99 };
            
            if (resultData["tid_status"] == "100"
                && payment_key != "27" && payment_key != "59")
            {
                PaymentResult.PaymentIsCompleted = true;                
            }
            else if((Array.Exists(onHoldStatus, x => x == System.Convert.ToInt32(resultData["tid_status"])))
                || (payment_key == "27" || payment_key == "59"))
            {
                PaymentResult.PaymentIsAuthorized = true;

                if ((payment_key == "27" || payment_key == "41") && resultData["tid_status"] == "100")
                {
                    PaymentResultItemInfo item = new PaymentResultItemInfo();
                    item.Header = ResHelper.GetString("custom.transaction_details") + ": ";
                    item.Name = "AccountHolder";
                    item.Value = ResHelper.GetString("custom.duedate_title") + ": " + resultData["due_date"] + " | " + ResHelper.GetString("custom.account_holder") + ": " + resultData["invoice_account_holder"] + " | " + "IBAN: " + resultData["invoice_iban"] + " | " + "BIC: " + resultData["invoice_bic"] + " | " + "Bank: " + resultData["invoice_bankname"] + " | " + ResHelper.GetString("custom.amount") + ": " + resultData["amount"] + " " + resultData["currency"] + " | " + ResHelper.GetString("custom.payment_reference_title") + " | " + ResHelper.GetString("custom.payment_reference1") + ": " + resultData["invoice_ref"] + " | " + ResHelper.GetString("custom.payment_reference2") + ":  " + resultData["tid"];     // Saves the custom item into the PaymentResultInfo object processed by the gateway provider
                    PaymentResult.SetPaymentResultItemInfo(item);
                }
                else if (payment_key == "59")
                {
                    PaymentResultItemInfo ShopDetails = new PaymentResultItemInfo();
                    ShopDetails.Header = "Slip expiry date :" + resultData["cashpayment_due_date"];
                    ShopDetails.Name = "Barzahlen";
                    ShopDetails.Value = "Store(s) near you : " + resultData["nearest_store_title_1"] + " | " + resultData["nearest_store_street_1"] + " | " + resultData["nearest_store_city_1"] + " | " + resultData["nearest_store_zipcode_1"] + " | " + resultData["nearest_store_country_1"] + resultData["nearest_store_title_2"] + " | " + resultData["nearest_store_street_2"] + " | " + resultData["nearest_store_city_2"] + " | " + resultData["nearest_store_zipcode_2"] + " | " + resultData["nearest_store_country_2"] + resultData["nearest_store_title_3"] + " | " + resultData["nearest_store_street_3"] + " | " + resultData["nearest_store_city_3"] + " | " + resultData["nearest_store_zipcode_3"] + " | " + resultData["nearest_store_country_3"];     // Saves the custom item into the PaymentResultInfo object processed by the gateway provider
                    PaymentResult.SetPaymentResultItemInfo(ShopDetails);
                }
                else if(resultData["tid_status"] == "75" && payment_key == "41")
                {
                    PaymentResult.PaymentDescription = "Your order is under verification and once confirmed, we will send you our bank details to where the order amount should be transferred. Please note that this may take upto 24 hours.";
                }
            }           
        }
        else
        {
            // Sets the payment result for failed transactions
            PaymentResult.PaymentIsFailed = true;
            PaymentResult.PaymentDescription = resultData["status_desc"];
        }       
        
        // Saves the payment result to the related order
        // Moves the order to the status configured for successful or failed payments
        UpdateOrderPaymentResult();        
    }
        
    /// <summary>
    /// Generate unique id with 16 digit unique number to use in the encryption of parameters.
    /// </summary>
    public static string getUniqueid()
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

        // Return the encrypted data as a string
        return cipherText;
    }   

    /// <summary>
    /// Generate hash value
    /// </summary>
    public static string getHash(string authcode, string product, string tariff, string amount, string testMode, string uniqid, string password)
    {
        SHA256 sha256 = SHA256Managed.Create();
        byte[] bytes = Encoding.UTF8.GetBytes(authcode + product + tariff + amount + testMode + uniqid + Reverse(password));
        byte[] hash = sha256.ComputeHash(bytes);
        return GetStringFromHash(hash);
    }
        
    /// <summary>
    /// Get string from hash value
    /// </summary>
    public static string GetStringFromHash(byte[] hash)
    {
        StringBuilder result = new StringBuilder();

        for (int i = 0; i < hash.Length; i++)
        {
            result.Append(hash[i].ToString("X2"));
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

}
