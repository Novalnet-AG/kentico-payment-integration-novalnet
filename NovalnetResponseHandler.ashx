<%@ WebHandler Language="C#" Class="NovalnetResponseHandler" %>
using System;
using System.Web;
using CMS.Ecommerce;
using System.IO;
using CMS.DataEngine;
using System.Text;
using System.Security.Cryptography;
using CMS.SiteProvider;

public class NovalnetResponseHandler :IHttpHandler
{
    public void ProcessRequest(HttpContext context)
    {
        // Payport response data
        var data = context.Request.Form;
        int orderNumber = Convert.ToInt32(data["order_no"]);
        OrderInfo order = OrderInfoProvider.GetOrderInfo(orderNumber);

        // Prepares an instance of the payment gateway provider used by the order's payment method       
        NovalnetGatewayProvider customProvider = CMSPaymentGatewayProvider.GetPaymentGatewayProvider<NovalnetGatewayProvider>(order.OrderPaymentOptionID);
        customProvider.OrderId = orderNumber;

        string shopTestMode = SettingsKeyInfoProvider.GetValue("test_mode", SiteContext.CurrentSiteID);
        string responseTestMode = "";
        string amount = data["amount"];
        string callbackAmount = "";
        int[] onHoldStatus = new int[] { };
        if(data["implementation"] == "ENC"
            && data["test_mode"] != "1"
            && data["test_mode"] != "0")
        {
            string password = SettingsKeyInfoProvider.GetValue("payment_access_key", SiteContext.CurrentSiteID);
            responseTestMode = DecryptString(data["test_mode"], password, data["uniqid"]);
            amount = DecryptString(data["amount"], password, data["uniqid"]);
            callbackAmount = (data["payment_type"]  == "PAYPAL" && (data["tid_status"] == "90" || data["tid_status"] == "85")) ? "0" : amount;

        } else
        {
            responseTestMode = data["test_mode"];
            decimal orderAmount = Convert.ToDecimal(data["amount"]) * 100;
            amount = Convert.ToInt32(orderAmount).ToString();            
        }
        string payment_key = "";
        if(data["key"] == null)
        {
            payment_key = data["payment_id"];
        }
        else
        {
            payment_key = data["key"];
        }
        string paymentType = (payment_key == "27") ? data["invoice_type"] : data["payment_type"];

        string testMode = (shopTestMode == "True" || responseTestMode == "1") ? "1" : "0";

        var NovalnetTransactionInfo = ConnectionHelper.ExecuteQuery("insert into Novalnet_TransactionDetail(TransactionID,GatewayStatus,PaymentId,PaymentType,Currency,OrderNo,TestMode,CustomerId,Amount,CallbackAmount) values('" + data["tid"] + "','" + data["tid_status"] + "','" + payment_key + "','" + paymentType + "','" + data["currency"] + "','" + data["order_no"] + "','" + testMode + "','" + data["customer_no"] + "','"+ amount +"','" + callbackAmount + "')", null, QueryTypeEnum.SQLQuery);

        if ((Convert.ToInt32(data["status"]) == 100) || (Convert.ToInt32(data["status"]) == 90))
        {
            customProvider.ProcessDirectPaymentReply(true, context, testMode);
            HttpResponse response = context.Response;
            response.Redirect("~/Special-Pages/Order-Completed.aspx");
        }
        else
        {
            customProvider.ProcessDirectPaymentReply(false, context, testMode);
            HttpResponse response = context.Response;
            response.Redirect("~/Special-Pages/Payment-Page.aspx?o="+data["inputval1"]+"&novalnet_error_message="+data["status_text"]);
        }
    }


    public bool IsReusable
    {
        get
        {
            return true;
        }
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
        finally
        {
            // Close both the MemoryStream and the CryptoStream
            memoryStream.Close();
            cryptoStream.Close();
        }

        // Return the decrypted data as a string
        return plainText;
    }

}
