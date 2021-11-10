<%@ WebHandler Language="C#" Class="NovalnetCallback" %>
using System.Web;
using System.Net;
using CMS.DataEngine;
using System.Collections.Generic;
using System.Linq;
using System.Data;
using System;
using CMS.Ecommerce;
using CMS.Helpers;
using CMS.EmailEngine;


public class NovalnetCallback :IHttpHandler
{
    // Initial payment types
    public string[] initialPayments = new string[]
    {
        "CREDITCARD",
        "INVOICE_START",
        "GUARANTEED_INVOICE",
        "DIRECT_DEBIT_SEPA",
        "GUARANTEED_DIRECT_DEBIT_SEPA",
        "PAYPAL",
        "PRZELEWY24",
        "ONLINE_TRANSFER",
        "IDEAL",
        "GIROPAY",
        "EPS",
        "CASHPAYMENT"
    };

    // Chargeback, book back and return debit payment types
    public string[] chargebackPayments =
    {
        "RETURN_DEBIT_SEPA",
        "CREDITCARD_BOOKBACK",
        "CREDITCARD_CHARGEBACK",
        "GUARANTEED_INVOICE_BOOKBACK",
        "GUARANTEED_SEPA_BOOKBACK",
        "PAYPAL_BOOKBACK",
        "REFUND_BY_BANK_TRANSFER_EU",
        "PRZELEWY24_REFUND",
        "CASHPAYMENT_REFUND",
        "REVERSAL"
    };

    // Credit and collection payment types
    public string[] collectionPayments =
    {
        "INVOICE_CREDIT",
        "CASHPAYMENT_CREDIT",
        "ONLINE_TRANSFER_CREDIT",
        "CREDIT_ENTRY_CREDITCARD",
        "CREDIT_ENTRY_SEPA",
        "CREDIT_ENTRY_DE",
        "DEBT_COLLECTION_CREDITCARD",
        "DEBT_COLLECTION_SEPA",
        "DEBT_COLLECTION_DE"
    };

    // Mandatory paramters
    public string[] requiredParams  =
    {
        "vendor_id",
        "status",
        "payment_type",
        "tid_status",
        "tid"
    };

    /// <summary>
    /// Receiving Novalnet vendor script(Asynchronous) data
    /// </summary>
    /// <param name="context"></param>
    public void ProcessRequest(HttpContext context)
    {
        // Validating the mandatory parameters and assign the original tid
        IDictionary<string, string> param = ValidateRequestParams(context);
        // Getting payment level case based on payment types
        string paymentType = GetPaymentTypeLevel(param, context);

        // Handle initial payment types
        if (paymentType == "zero_level")
        {
            InitialLevelPayments(param, context);
        }
        // Handle chargeback, book back and return debit payment types
        else if (paymentType == "first_level")
        {
            HandleChargebackPayments(param, context);
        }
        // Handle credit and collection payment types
        else if (paymentType == "second_level")
        {
            CreditEntryPayment(param, context);
        }
        // Handle transaction cancellation
        else if (paymentType == "cancellation")
        {
            IDictionary<string, object> nntransHistory = GetOrderReference(param,context);
            OrderInfo orderInfo = new OrderInfo();
            orderInfo = OrderInfoProvider.GetOrderInfo(Convert.ToInt32(nntransHistory["OrderNo"]));
            int[] pendingStatus = new int[] { 75, 91, 99, 98, 85 };
            if((Array.Exists(pendingStatus, x => x == Convert.ToInt32(nntransHistory["GatewayStatus"])))
             && param["payment_type"] == "TRANSACTION_CANCELLATION")
            {
                ConnectionHelper.ExecuteQuery("update Novalnet_TransactionDetail set GatewayStatus ="+ param["tid_status"] +" where transactionid = " + param["shop_tid"] + " ",null,QueryTypeEnum.SQLQuery, true);
                DateTime now = DateTime.Now;
                string CallbackComment = "Novalnet callback received. The transaction has been canceled on " + now + "";
                var orderStatus = PaymentOptionInfoProvider.GetPaymentOptionInfo(orderInfo.OrderPaymentOptionID);
                orderInfo.OrderIsPaid = false;
                ConnectionHelper.ExecuteQuery("update com_order set orderispaid =  0,orderstatusid = " + orderStatus.PaymentOptionFailedOrderStatusID +" where orderid = '" + nntransHistory["OrderNo"] + "'", null, QueryTypeEnum.SQLQuery);
                InsertCallbackComments(CallbackComment,orderInfo);
                SendNotificationMail(CallbackComment,nntransHistory["OrderNo"].ToString());
                PrintMessage(CallbackComment,context);
            }
            else
            {
                PrintMessage("Novalnet callback received. Payment type is not applicable for this process", context);
            }
        }
        else
        {
            PrintMessage("Novalnet callback received. Payment type is not applicable for this process", context);
        }
    }

    /// <summary>
    /// Validating the received parameters
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    public IDictionary<string, string> ValidateRequestParams(HttpContext context)
    {

        var parameters = context.Request.QueryString;
        string clientIP = GetClientIP(context);
        // Getting IP using host name
        string hostIP = null;
        IPAddress[] hostIPDetails = Dns.GetHostAddresses("pay-nn.de");
        foreach (IPAddress ip in hostIPDetails)
        {
            hostIP = ip.ToString();
        }

        // Host IP is missing
        if (string.IsNullOrEmpty(hostIP) || hostIP == "")
        {
            PrintMessage("Novalnet HOST IP missing", context);
        }

        // IP validation for manual testing
        bool deactivateIP = SettingsKeyInfoProvider.GetBoolValue("deactivate_ip");
        if (hostIP != clientIP && deactivateIP == false)
        {
            PrintMessage("Novalnet callback received. Unauthorised access from the IP " + clientIP, context);
        }

        var collectionsAndChargebacksList = new List<string>();
        collectionsAndChargebacksList.AddRange(chargebackPayments);
        collectionsAndChargebacksList.AddRange(collectionPayments);
        string[] collectionsAndChargebacks = collectionsAndChargebacksList.ToArray();

        var mandatoryParamList = new List<string>();
        var captureParams = new Dictionary<string, string>();
        foreach (string data in context.Request.QueryString.Keys)
        {
            captureParams[data] = context.Request.QueryString[data];
        }

        // If subs_billing received, assign signup_tid as mandatory
        if (captureParams.ContainsKey("subs_billing") && parameters["subs_billing"] == "1")
        {
            mandatoryParamList.Add("signup_tid");
            captureParams["shop_tid"] = parameters["signup_tid"];
        }
        // If credit and collection payment types received, assign tid_payment as mandatory
        else if (collectionsAndChargebacks.Contains(parameters["payment_type"]) == true)
        {
            mandatoryParamList.Add("tid_payment");
            captureParams["shop_tid"] = parameters["tid_payment"];
        }
        // Assign tid as mandatory for remaining payment types
        else
        {
            mandatoryParamList.Add("tid");
            captureParams["shop_tid"] = parameters["tid"];
        }

        mandatoryParamList.AddRange(requiredParams);
        string[] request = mandatoryParamList.ToArray();

        // Validate mandatory parameters
        string[] tidList = { "tid", "tid_payment", "signup_tid" };
        foreach (string param in request)
        {
            if (!captureParams.ContainsKey(param) || string.IsNullOrEmpty(captureParams[param]))
            {
                PrintMessage("Required param (" + param + " ) missing!", context);
            }
            else if (tidList.Contains(param) && !System.Text.RegularExpressions.Regex.IsMatch(captureParams[param], "^[0-9]{17}$"))
            {
                PrintMessage("Novalnet callback received. Invalid TID [ " + captureParams[param] + " ] ",context);
            }
        }

        // Validate payment type
        var paymentTypeList = new List<string>();
        paymentTypeList.AddRange(collectionsAndChargebacks);
        paymentTypeList.AddRange(initialPayments);
        paymentTypeList.Add("TRANSACTION_CANCELLATION");
        string[] CollectionChargebacksAndPayments = paymentTypeList.ToArray();

        if (!CollectionChargebacksAndPayments.Contains(captureParams["payment_type"]) || string.IsNullOrEmpty(captureParams["payment_type"]))
        {
            PrintMessage("Novalnet callback received. Payment type ( " + captureParams["payment_type"] + " ) is mismatched!",context);
        }
        return captureParams;
    }

    /// <summary>
    /// Getting client IP Address
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    public string GetClientIP(HttpContext context)
    {
        string ipAddress;
        ipAddress = context.Request.ServerVariables["HTTP_X_FORWARDED_FOR"];
        if (ipAddress == null)
        {
            ipAddress = context.Request.ServerVariables["REMOTE_ADDR"];
        }
        return ipAddress;
    }

    /// <summary>
    /// Get payment_type level
    /// </summary>
    /// <param name="param"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public string GetPaymentTypeLevel(IDictionary<string, string> param, HttpContext context)
    {
        // Initial payments - Level : 0
        if (initialPayments.Contains(param["payment_type"]))
        {
            return "zero_level";
        }
        // Chargeback payment types - Level : 1
        else if (chargebackPayments.Contains(param["payment_type"]))
        {
            return "first_level";
        }
        // Credit entry and collection payment types - Level: 2
        else if (collectionPayments.Contains(param["payment_type"]))
        {
            return "second_level";
        }
        else if (param["payment_type"] == "TRANSACTION_CANCELLATION")
        {
            return "cancellation";
        }
        return "error";
    }

    /// <summary>
    /// Get order reference from the Novalnet_TransactionDetail table on shop database
    /// </summary>
    /// <param name="parameters"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public IDictionary<string, object> GetOrderReference(IDictionary<string, string> parameters, HttpContext context)
    {
        var captureParams = new Dictionary<string, string>();
        var dictionary = new Dictionary<string, object>();
        DataSet ds = ConnectionHelper.ExecuteQuery("SELECT * FROM Novalnet_TransactionDetail where TransactionID ="+ parameters["shop_tid"], null, QueryTypeEnum.SQLQuery, true);
        foreach (DataRow row in ds.Tables[0].Rows)
        {
            dictionary = Enumerable.Range(0, ds.Tables[0].Columns.Count).ToDictionary(i => ds.Tables[0].Columns[i].ColumnName, i => row.ItemArray[i]);
        }
        object orderInfo = null;
        if(dictionary.ContainsKey("OrderNo"))
        {
            orderInfo =  OrderInfoProvider.GetOrderInfo(Convert.ToInt32(dictionary["OrderNo"]));
        }
        else if (parameters.ContainsKey("order_no"))
        {
            orderInfo = OrderInfoProvider.GetOrderInfo(Convert.ToInt32(parameters["order_no"]));
            if(parameters.ContainsKey("order_no") && parameters["order_no"] != dictionary["OrderNo"].ToString())
            {
                PrintMessage("Novalnet callback received. Order no is not valid", context);
            }
            if (orderInfo == null)
            {
                CriticalMailComments(parameters, context);
            }
        }

        // If transaction not found in Novalnet table but the order number available in Novalnet system, handle communication break
        if (ds.Tables[0].Rows.Count == 0 && orderInfo != null)
        {
            CommunicationFailure(parameters, context);
        }

        else if (ds.Tables[0].Rows.Count == 0)
        {
            PrintMessage("Novalnet callback received. order reference not found in the database", context);
        }

        return dictionary;
    }

    /// <summary>
    /// Handle initial payment types
    /// </summary>
    /// <param name="parameter"></param>
    /// <param name="context"></param>        
    public void InitialLevelPayments(IDictionary<string, string> parameter, HttpContext context)
    {
        IDictionary<string, object> nntransHistory = GetOrderReference(parameter, context);
        var orderInfo = OrderInfoProvider.GetOrderInfo(Convert.ToInt32(nntransHistory["OrderNo"]));
        int amount = parameter.ContainsKey("amount") ? Convert.ToInt32(parameter["amount"]) : 0;
        string currency = parameter.ContainsKey("currency") ? parameter["currency"] : CurrencyInfoProvider.GetCurrencyInfo(orderInfo.OrderCurrencyID).CurrencyCode;

        DateTime now = DateTime.Now;
        var orderStatus = PaymentOptionInfoProvider.GetPaymentOptionInfo(orderInfo.OrderPaymentOptionID);
        int CallbackAmount = (nntransHistory["CallbackAmount"] is DBNull) ? 0 : Convert.ToInt32(nntransHistory["CallbackAmount"]);
        int orderPaidAmount = Convert.ToInt32(amount) + CallbackAmount;
        string[] pendingPayment = new string[]
        {
            "INVOICE_START",
            "GUARANTEED_INVOICE",
            "DIRECT_DEBIT_SEPA",
            "GUARANTEED_DIRECT_DEBIT_SEPA",
            "CREDITCARD"
        };
        int[] gatewayStatus = new int[] { 75, 91, 98, 99 };
        int[] responseStatus = new int[] { 100, 99, 91, 98, 85 };
        var callbackParams = new Dictionary<string, string>()  {
                                                {"PaymentType",parameter["payment_type"]},
                                                {"Status",parameter["tid_status"]},
                                                {"TransactionID",parameter["tid"]},
                                                {"Currency",parameter.ContainsKey("currency") ? parameter["currency"] : CurrencyInfoProvider.GetCurrencyInfo(orderInfo.OrderCurrencyID).CurrencyCode},
                                                {"TidPayment",nntransHistory["TransactionID"].ToString()},
                                                {"PaidAmount", orderPaidAmount.ToString()},
                                                {"OrderNo", nntransHistory["OrderNo"].ToString()},
                                                {"Amount",amount.ToString()},
        };
        if (parameter["payment_type"] == "PAYPAL")
        {
            if (parameter["tid_status"] == "100" && Convert.ToString(nntransHistory["CallbackAmount"]) == "0")
            {
                string callbackComments = "Novalnet Callback Script executed successfully for the TID: " + parameter["tid"] + " with amount " + Convert.ToDecimal(amount)/100 + " " + currency +" on " + now + " .";
                orderInfo.OrderIsPaid = true;
                ConnectionHelper.ExecuteQuery("update com_order set orderispaid =  1,orderstatusid = " + orderStatus.PaymentOptionSucceededOrderStatusID + " where orderid = '" + nntransHistory["OrderNo"] + "'", null, QueryTypeEnum.SQLQuery);
                ConnectionHelper.ExecuteQuery("update novalnet_transactiondetail set GatewayStatus = " + parameter["tid_status"] + ",CallbackAmount = " + amount + " where orderno = " + nntransHistory["OrderNo"] + "", null, QueryTypeEnum.SQLQuery);
                InsertCallbackTable(callbackParams);
                SendNotificationMail(callbackComments, nntransHistory["OrderNo"].ToString());
                InsertCallbackComments(callbackComments, orderInfo);
                PrintMessage(callbackComments,context);
            }
            else
            {
                PrintMessage("Novalnet callback received. Order already paid.", context);
            }
        }
        else if(parameter["payment_type"] == "PRZELEWY24")
        {
            if (parameter["tid_status"] == "100")
            {
                if (Convert.ToString(nntransHistory["CallbackAmount"]) == "0")
                {
                    string callbackComments = "Novalnet Callback Script executed successfully for the TID: " + parameter["tid"] + " with amount " + Convert.ToDecimal(amount)/100 + " " + currency + " on " + now + " .";
                    orderInfo.OrderIsPaid = true;
                    ConnectionHelper.ExecuteQuery("update com_order set orderispaid =  1,orderstatusid = " + orderStatus.PaymentOptionSucceededOrderStatusID + " where orderid = '" + nntransHistory["OrderNo"] + "'", null, QueryTypeEnum.SQLQuery);
                    ConnectionHelper.ExecuteQuery("update novalnet_transactiondetail set GatewayStatus = " + parameter["tid_status"] + ",CallbackAmount = " + amount + "  where orderno = " + nntransHistory["OrderNo"] + "", null, QueryTypeEnum.SQLQuery);
                    InsertCallbackTable(callbackParams);
                    SendNotificationMail(callbackComments,nntransHistory["OrderNo"].ToString());
                    InsertCallbackComments(callbackComments,orderInfo);
                    PrintMessage(callbackComments, context);
                }
                else
                {
                    PrintMessage("Novalnet callback received. Order already paid.", context);
                }
            }
            else
            {
                string message = ((parameter.ContainsKey("status_message")) ? parameter["status_message"] : ((parameter.ContainsKey("status_text")) ? parameter["status_text"] : ((parameter.ContainsKey("status_desc")) ? parameter["status_desc"] : "Payment was not successful. An error occurred")));
                string callbackComments = "The transaction has been canceled due to: " + message + "";
                ConnectionHelper.ExecuteQuery("update com_order set orderstatusid = " + orderStatus.PaymentOptionFailedOrderStatusID + " where orderid = '" + nntransHistory["OrderNo"] + "'", null, QueryTypeEnum.SQLQuery);
                ConnectionHelper.ExecuteQuery("update novalnet_transactiondetail set GatewayStatus = " + parameter["tid_status"] + ",CallbackAmount = " + amount + "  where orderno = " + nntransHistory["OrderNo"] + "", null, QueryTypeEnum.SQLQuery);
                InsertCallbackTable(callbackParams);
                SendNotificationMail(callbackComments,nntransHistory["OrderNo"].ToString());
                InsertCallbackComments(callbackComments, orderInfo);
                PrintMessage(callbackComments, context);
            }

        }
        else if(Array.Exists(pendingPayment, x => x == parameter["payment_type"]) && Array.Exists(gatewayStatus, x=> x == Convert.ToInt32(nntransHistory["GatewayStatus"])) && Array.Exists(responseStatus, x=> x == Convert.ToInt32(parameter["tid_status"])))
        {
            int status = (parameter["tid_status"] == "100" && (parameter["payment_type"] != "INVOICE_START")) ? orderStatus.PaymentOptionSucceededOrderStatusID : orderInfo.OrderStatusID;
            int orderispaid = (parameter["tid_status"] == "100" && (parameter["payment_type"] != "INVOICE_START")) ? 1 : 0;
            orderInfo.OrderIsPaid = (parameter["tid_status"] == "100" && (parameter["payment_type"] != "INVOICE_START")) ? true : false;
            string callbackComments = (parameter["tid_status"] == "100") ? "Novalnet callback received. The transaction has been confirmed on " + now + "" : "Novalnet callback received. The transaction status has been changed from pending to on hold for the TID: " + parameter["tid"] + " on " + now + ".";
            ConnectionHelper.ExecuteQuery("update com_order set orderstatusid = " + status + ",orderispaid = " + orderispaid + " where orderid = " + nntransHistory["OrderNo"] + "", null, QueryTypeEnum.SQLQuery);
            ConnectionHelper.ExecuteQuery("update novalnet_transactiondetail set gatewaystatus = " + parameter["tid_status"] + " where orderno = " + nntransHistory["OrderNo"] + "", null, QueryTypeEnum.SQLQuery);
            InsertCallbackTable(callbackParams);
            InsertCallbackComments(callbackComments,orderInfo);
            SendNotificationMail(callbackComments,nntransHistory["OrderNo"].ToString());
            if((parameter["payment_type"] == "INVOICE_START" || parameter["payment_type"] == "GUARANTEED_INVOICE") && parameter["tid_status"] == "100")
            {
                ShowBankDetails(nntransHistory,orderInfo, parameter);
            }
            PrintMessage(callbackComments, context);
        }
        else
        {
            PrintMessage("Novalnet callback received. Payment type " + parameter["payment_type"] + "  is not applicable for this process",context);
        }

    }
    /// <summary>
    /// Handle chargeback, book back and return debit payment types
    /// </summary>
    /// <param name="data"></param>
    /// <param name="context"></param>
    public void HandleChargebackPayments(IDictionary<string,string> data, HttpContext context)
    {
        IDictionary<string, object> nntransHistory = GetOrderReference(data,context);
        int amount = data.ContainsKey("amount") ? Convert.ToInt32(data["amount"]) : 0;
        var orderInfo = OrderInfoProvider.GetOrderInfo(Convert.ToInt32(nntransHistory["OrderNo"]));
        string currency = data.ContainsKey("currency") ? data["currency"] : CurrencyInfoProvider.GetCurrencyInfo(orderInfo.OrderCurrencyID).CurrencyCode;
        string[] Bookback = new string[] { "PAYPAL_BOOKBACK", "CREDITCARD_BOOKBACK", "PRZELEWY24_REFUND", "GUARANTEED_INVOICE_BOOKBACK", "GUARANTEED_SEPA_BOOKBACK", "CASHPAYMENT_REFUND", "REFUND_BY_BANK_TRANSFER_EU" };
        DateTime now = DateTime.Now;
        string comments = Array.Exists(Bookback, x => x == data["payment_type"]) ? "Refund/Bookback" : "Chargeback";
        string chargebackComments = "Novalnet callback received. " + comments + " executed successfully for the TID: " + data["shop_tid"] + " amount: " + Convert.ToDecimal(amount)/100 + " " + currency + " on " + now + ". The subsequent TID: " + data["tid"];
        InsertCallbackComments(chargebackComments,orderInfo);
        SendNotificationMail(chargebackComments,nntransHistory["OrderNo"].ToString());
        PrintMessage(chargebackComments,context);

    }
    /// <summary>
    /// Handle credit and collection payment types
    /// </summary>
    /// <param name="data"></param>
    /// <param name="context"></param>
    public void CreditEntryPayment(IDictionary<string,string> data, HttpContext context)
    {
        IDictionary<string, object> nntransHistory = GetOrderReference(data,context);
        int amount = data.ContainsKey("amount") ? Convert.ToInt32(data["amount"]) : 0;
        OrderInfo orderInfo = new OrderInfo();
        orderInfo = OrderInfoProvider.GetOrderInfo(Convert.ToInt32(nntransHistory["OrderNo"]));
        string currency = data.ContainsKey("currency") ? data["currency"] : CurrencyInfoProvider.GetCurrencyInfo(orderInfo.OrderCurrencyID).CurrencyCode;
        IDictionary<string, object> callbackHistory = GetCallbackdetails(data["shop_tid"].ToString());
        int orderGrandTotal = Convert.ToInt32(orderInfo.OrderGrandTotal * 100);
        int CallbackAmount = (nntransHistory["CallbackAmount"] is DBNull) ? 0 : Convert.ToInt32(nntransHistory["CallbackAmount"]);
        int orderPaidAmount = Convert.ToInt32(amount) + CallbackAmount;
        if (orderPaidAmount <= orderGrandTotal)
        {
            // orderPaidAmount = Convert.ToInt32(amount) + CallbackAmount;
            int CurrentAmount = orderPaidAmount - orderGrandTotal;
            DateTime now = DateTime.Now;
            var callbackParams = new Dictionary<string, string>()  {
                                                {"PaymentType",data["payment_type"]},
                                                {"Status",data["tid_status"]},
                                                {"TransactionID",data["tid"]},
                                                {"Currency",data.ContainsKey("currency") ? data["currency"] : CurrencyInfoProvider.GetCurrencyInfo(orderInfo.OrderCurrencyID).CurrencyCode},
                                                {"TidPayment",data["tid_payment"]},
                                                {"PaidAmount", orderPaidAmount.ToString()},
                                                {"OrderNo", nntransHistory["OrderNo"].ToString()},
                                                {"Amount",amount.ToString()},
                                            };
            InsertCallbackTable(callbackParams);
            ConnectionHelper.ExecuteQuery("update Novalnet_TransactionDetail set CallbackAmount = '" + orderPaidAmount + "' where TransactionID = '" + data["tid_payment"] + "'", null, QueryTypeEnum.SQLQuery);
            string callbackComments = "Novalnet Callback Script executed successfully for the TID:" + data["tid_payment"] + " with amount " + Convert.ToDecimal(amount)/100 + " " + currency + "  on " + now + ". Please refer PAID transaction in our Novalnet Merchant Administration with the TID:" + data["tid"];
            if (orderPaidAmount >= orderGrandTotal)
            {
                var orderStatus = PaymentOptionInfoProvider.GetPaymentOptionInfo(orderInfo.OrderPaymentOptionID);
                orderInfo.OrderIsPaid = true;
                ConnectionHelper.ExecuteQuery("update com_order set orderispaid =  1,orderstatusid = " + orderStatus.PaymentOptionSucceededOrderStatusID +" where orderid = '" + nntransHistory["OrderNo"] + "'", null, QueryTypeEnum.SQLQuery);

            }
            InsertCallbackComments(callbackComments,orderInfo);
            SendNotificationMail(callbackComments,nntransHistory["OrderNo"].ToString());
            PrintMessage(callbackComments,context);
        }
        PrintMessage("Novalnet callback received. Callback Script executed already. Refer Order :" + nntransHistory["OrderNo"],context);
    }
    /// <summary>
    ///  Handling communication failure
    /// </summary>
    /// <param name="data"></param>
    /// <param name="context"></param>
    public void CommunicationFailure(IDictionary<string,string> data, HttpContext context)
    {
        int[] successTransaction = new int[] { 100, 99, 98, 91, 90, 86, 85, 75 };
        int[] onHoldTidStatus = new int[] { 99, 98, 91, 90, 86, 85, 75 };
        int[] successStatus = new int[] { 90, 100 };
        var orderInfo = OrderInfoProvider.GetOrderInfo(Convert.ToInt32(data["order_no"]));
        var orderStatus = PaymentOptionInfoProvider.GetPaymentOptionInfo(orderInfo.OrderPaymentOptionID);
        int orderStatusId = Array.Exists(onHoldTidStatus,x => x == Convert.ToInt32(data["tid_status"])) ?  orderStatus.PaymentOptionAuthorizedOrderStatusID : orderStatus.PaymentOptionSucceededOrderStatusID;
        string currency = data.ContainsKey("currency") ? data["currency"] : CurrencyInfoProvider.GetCurrencyInfo(orderInfo.OrderCurrencyID).CurrencyCode;
        string customerNo = data.ContainsKey("customer_no") ? data["customer_no"] : orderInfo.OrderCustomerID.ToString();

        // Handle success transaction
        if(successTransaction.Contains(Convert.ToInt32(data["tid_status"])) && successStatus.Contains(Convert.ToInt32(data["status"])))
        {
            ConnectionHelper.ExecuteQuery("update com_order set orderstatusid = " + orderStatusId + ",orderispaid = 1 where orderId = " + data["order_no"] +" ", null, QueryTypeEnum.SQLQuery);
            // Store order details in Novalnet table
            ConnectionHelper.ExecuteQuery("insert into novalnet_transactiondetail (TransactionID,OrderNo,Currency,Amount,PaymentId,PaymentType,CustomerId,TestMode,GatewayStatus) values ('" + data["tid"] + "' , '" + data["order_no"] + "','" + currency + "' , '" + data["amount"] + "','" + data["payment_id"] + "' , '" + data["payment_type"] + "' , '" + customerNo + "','" + data["test_mode"] + "','" + data["tid_status"] + "')", null, QueryTypeEnum.SQLQuery);
            string comments = "Novalnet Callback Script executed successfully.Novalnet transaction ID " + data["tid"] + "";
            InsertCallbackComments(comments, orderInfo,data["test_mode"]);
            PrintMessage(comments,context);
        }
        // Handle failure transaction
        else
        {
            ConnectionHelper.ExecuteQuery("update com_order set orderstatusid = " + orderStatus.PaymentOptionFailedOrderStatusID + ",orderispaid = 0 where orderid = " + data["order_no"] + "",null,QueryTypeEnum.SQLQuery);
            // Store order details in Novalnet table
            ConnectionHelper.ExecuteQuery("insert into novalnet_transactiondetail (TransactionID,OrderNo,Status,Currency,Amount,PaymentId,PaymentType,CustomerId,TestMode,GatewayStatus) values ('" + data["tid"] + "','" + data["order_no"] + "', '" + data["status"] + "' , '" + currency + "' , '" + data["amount"] + "' , '" + data["payment_id"] + "', '" + data["payment_type"] + "','" + customerNo + "','" + data["test_mode"] + "','" + data["tid_status"] + "')", null, QueryTypeEnum.SQLQuery);
            string errorMessage = data.ContainsKey("status_text") ? data["status_text"] : data.ContainsKey("status_desc") ? data["status_desc"] : "Payment was not successful. An error occurred";
            string tidLabel = "Novalnet transaction ID " + data["tid"] + "";
            string comments = "Novalnet Callback Script executed successfully. " + errorMessage + ". " + tidLabel +".";
            InsertCallbackComments(comments,orderInfo,data["test_mode"].ToString());
            PrintMessage(comments, context);
        }
    }
    /// <summary>
    /// Insert callback details in callback history table
    /// </summary>
    /// <param name="data"></param>
    public void InsertCallbackTable(IDictionary<string,string> data)
    {
        int paidAmount = data.ContainsKey("PaidAmount") ? Convert.ToInt32(data["PaidAmount"]) : Convert.ToInt32(null);
        ConnectionHelper.ExecuteQuery("insert into Novalnet_CallbackHistory(PaymentType,Status,Currency,OrderNo,TidPayment,PaidAmount,Amount,TransactionID) values('" + data["PaymentType"] + "','" + data["Status"] + "','" + data["Currency"] + "','" + data["OrderNo"] + "','" + data["TidPayment"] + "','" + paidAmount + "','" + data["Amount"] + "','" + data["TransactionID"] + "')", null, QueryTypeEnum.SQLQuery);
    }

    /// <summary>
    /// Collect callback information from the novalnet callback history table
    /// </summary>
    /// <param name="tid"></param>
    /// <returns></returns>
    public IDictionary<string,object> GetCallbackdetails(string tid)
    {
        var callbackHistory = new Dictionary<string, object>();
        DataSet ds = ConnectionHelper.ExecuteQuery("SELECT * FROM Novalnet_CallbackHistory where TransactionID ="+ tid, null,QueryTypeEnum.SQLQuery, true);
        if (ds.Tables[0].Rows.Count == 0)
        {
            return null;
        }
        foreach (DataRow row in ds.Tables[0].Rows)
        {
            callbackHistory = Enumerable.Range(0, ds.Tables[0].Columns.Count).ToDictionary(i => ds.Tables[0].Columns[i].ColumnName, i => row.ItemArray[i]);
        }
        return callbackHistory;
    }

    /// <summary>
    /// Printing the success/ error messages
    /// </summary>
    /// <param name="message"></param>
    /// <param name="context"></param>
    private void PrintMessage(string message, HttpContext context)
    {
        context.Response.Write(message);
        context.Response.End();
    }
    /// <summary>
    ///  Insert callback comments
    /// </summary>
    /// <param name="data"></param>
    /// <param name="orderInfo"></param>
    /// <param name="testMode"></param>
    public void InsertCallbackComments(string data, OrderInfo orderInfo, string testMode = "")
    {
        if(testMode == "1")
        {
            PaymentResultItemInfo testModeText = new PaymentResultItemInfo();
            testModeText.Header = ResHelper.GetString("custom.transaction_mode") + ": ";
            testModeText.Name = "PaymentMode";
            testModeText.Value = (testMode == "1") ? ResHelper.GetString("custom.test_mode") : ResHelper.GetString("custom.live_mode");
            orderInfo.OrderPaymentResult.SetPaymentResultItemInfo(testModeText);
        }
        PaymentResultItemInfo customItem = new PaymentResultItemInfo();
        customItem.Header = "";
        customItem.Name = data;
        customItem.Value = data;
        orderInfo.OrderPaymentResult.SetPaymentResultItemInfo(customItem);
    }
    /// <summary>
    /// Show bank details for invoice payment
    /// </summary>
    /// <param name="nntransHistory"></param>
    /// <param name="orderInfo"></param>
    /// <param name="data"></param>

    public void ShowBankDetails(IDictionary<string,object> nntransHistory,OrderInfo orderInfo,IDictionary<string,string> data)
    {

        string dueDate = data.ContainsKey("due_date") ? data["due_date"] : "";
        string invoiceAccountHolder = data.ContainsKey("invoice_account_holder") ? data["invoice_account_holder"] : "";
        string invoiceIBAN = data.ContainsKey("invoice_iban") ? data["invoice_iban"] : "";
        string invoiceBankname = data.ContainsKey("invoice_bankname") ? data["invoice_bankname"] : "";
        string amount = data.ContainsKey("amount") ? data["amount"] : "";
        string invoiceBIC = data.ContainsKey("invoice_bic") ? data["invoice_bic"] : "";
        string invoiceRef = data.ContainsKey("invoice_ref") ? data["invoice_ref"] : "";
        PaymentResultItemInfo item = new PaymentResultItemInfo();
        item.Header = ResHelper.GetString("custom.transaction_details") + ": ";
        item.Name = data["shop_tid"];
        item.Value = ResHelper.GetString("custom.duedate_title") + ": " + dueDate + " | " + ResHelper.GetString("custom.account_holder") + ": " + invoiceAccountHolder + " | " + "IBAN: " + invoiceIBAN + " | " + "BIC: " + invoiceBIC + " | " + "Bank: " + invoiceBankname + " | " + ResHelper.GetString("custom.amount") + ": " + amount + " " + nntransHistory["Currency"] + " | " + ResHelper.GetString("custom.payment_reference_title") + " | " + ResHelper.GetString("custom.payment_reference1") + ": " + invoiceRef + " | " + ResHelper.GetString("custom.payment_reference2") + ":  " + data["shop_tid"];
        // Saves the custom item into the PaymentResultInfo object processed by the gateway provider
        orderInfo.OrderPaymentResult.SetPaymentResultItemInfo(item);
    }
    /// <summary>
    ///  Trigger crictical mail for transaction not found in Novalnet table
    /// </summary>
    /// <param name="data"></param>
    /// <param name="context"></param>
    public void CriticalMailComments(IDictionary<string,string> data, HttpContext context)
    {
        string merchantId = data.ContainsKey("vendor_id") ? data["vendor_id"] : "";
        string projectId = data.ContainsKey("product_id") ? data["product_id"] : "";
        string orderNo = data.ContainsKey("order_no") ? data["order_no"] : "";
        string email = data.ContainsKey("email") ? data["email"] : "";
        string newLine  = "<br>";
        string comments = "Dear Technic team,"+ newLine +" Please evaluate this transaction and contact our payment module team at Novalnet." + newLine  +" Merchant ID: " +data["vendor_id"]+ newLine + "Project ID: " + projectId + newLine +" TID: " + data["shop_tid"] + newLine + "TID status:" + data["tid_status"] + newLine + " Order no: " + orderNo + newLine + " Payment type:" + data["payment_type"] +newLine+ " E-mail:" + email +"";
        SendNotificationMail(comments, orderNo, data["shop_tid"].ToString(), true);
        PrintMessage(comments, context);
    }
    /// <summary>
    /// Send notification mail to Merchant
    /// </summary>
    /// <param name="comments"></param>
    /// <param name="orderNo"></param>
    /// <param name="tid"></param>
    /// <param name="missingTransactionNotify"></param>
    public void SendNotificationMail(string comments, string orderNo, string tid="", bool missingTransactionNotify = false)
    {
        bool enableEmail = SettingsKeyInfoProvider.GetBoolValue("callback_email");
        string emailTo = SettingsKeyInfoProvider.GetValue("email_to");
        string emailFrom = SettingsKeyInfoProvider.GetValue("CMSAdminEmailAddress");
        string mailTo = "";
        string mailSubject = "";
        // Initialize message
        //This is only for missing transaction notification
        if (missingTransactionNotify)
        {
            mailTo = "technic@novalnet.de";
            mailSubject = "Critical error on shop system order not found for TID: " + tid + "";
        }
        else if (enableEmail && !string.IsNullOrEmpty(emailTo))
        {
            mailTo = SettingsKeyInfoProvider.GetValue("email_to");
            mailSubject = "Novalnet Callback script notification - Order No : " + orderNo +"";

        }
        EmailMessage email = new EmailMessage();
        email.From = emailFrom;
        email.Recipients = mailTo;
        if (SettingsKeyInfoProvider.GetValue("email_bcc") != "")
        {
            email.BccRecipients = SettingsKeyInfoProvider.GetValue("email_bcc");
        }
        email.Subject = TextHelper.LimitLength(mailSubject.Trim(), 1000);
        email.EmailFormat = EmailFormatEnum.Html;
        email.Body = comments;

        // Initialize SMTP server object
        SMTPServerInfo smtpServer = new SMTPServerInfo
        {
            ServerName = SettingsKeyInfoProvider.GetValue("CMSSMTPServer"),
            ServerUserName = SettingsKeyInfoProvider.GetValue("CMSSMTPServerUser"),
#pragma warning disable 618
            ServerPassword = EncryptionHelper.EncryptData(SettingsKeyInfoProvider.GetValue("CMSSMTPServerPassword")),
#pragma warning restore 618
            ServerUseSSL = Convert.ToBoolean(SettingsKeyInfoProvider.GetValue("CMSUseSSL"))
        };
        string siteName = CMS.SiteProvider.SiteContext.CurrentSiteName ?? string.Empty;
        try
        {
            EmailSender.SendTestEmail(siteName, email, smtpServer);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
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
