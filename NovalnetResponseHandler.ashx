<%@ WebHandler Language="C#" Class="NovalnetResponseHandler" %>
using System;
using System.Web;
using CMS.Ecommerce;
using CMS.DataEngine;
using CMS.SiteProvider;
using NovalnetPayment;
using System.Text.RegularExpressions;
public class NovalnetResponseHandler : IHttpHandler
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
        string paymentKey = NovalnetHelper.GetPaymentKey(data);
        string responseTestMode = "";
        string amount = data["amount"];
        if ((data["implementation"] == "ENC" && data["test_mode"] != "" && data["uniqid"] != null) && !Regex.IsMatch(data["test_mode"], "^(?:1|0)"))
        {
            string password = SettingsKeyInfoProvider.GetValue("payment_access_key", SiteContext.CurrentSiteID);
            responseTestMode = NovalnetHelper.DecryptString(data["test_mode"], password, data["uniqid"]);
            amount = NovalnetHelper.DecryptString(data["amount"], password, data["uniqid"]);
        }
        else
        {
            responseTestMode = data["test_mode"];
            amount = NovalnetHelper.GetOrderAmount(Convert.ToDecimal(data["amount"])).ToString();
        }

        string paymentType = (paymentKey == "27") ? data["invoice_type"] : data["payment_type"];
        string testMode = (shopTestMode == "True" || responseTestMode == "1") ? "1" : "0";

        ConnectionHelper.ExecuteQuery("insert into Novalnet_TransactionDetail(TransactionID,GatewayStatus,PaymentId,PaymentType,Currency,OrderNo,TestMode,CustomerId,Amount) values('" + data["tid"] + "','" + data["tid_status"] + "','" + paymentKey + "','" + paymentType + "','" + data["currency"] + "','" + data["order_no"] + "','" + testMode + "','" + data["customer_no"] + "','" + amount + "')", null, QueryTypeEnum.SQLQuery);

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
            response.Redirect("~/Special-Pages/Payment-Page.aspx?o=" + data["inputval1"] + "&novalnet_error_message=" + data["status_text"]);
        }
    }

    public bool IsReusable
    {
        get
        {
            return true;
        }
    }

}

