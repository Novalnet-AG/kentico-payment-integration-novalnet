using System;
using System.Web;
using System.Collections.Generic;
using CMS.Ecommerce;
using System.Web.UI;
using CMS.DataEngine;
using System.Text;
using CMS.Helpers;
using CMS.SiteProvider;
using System.Text.RegularExpressions;
using NovalnetPayment;

public class NovalnetGatewayProvider : CMSPaymentGatewayProvider, IDirectPaymentGatewayProvider
{

    /// <summary>
    /// Validates the vendor credentials.    
    /// </summary>
    public override string ValidateCustomData(IDictionary<string, object> paymentData)
    {
        Dictionary<string, string> vendorDetails = NovalnetHelper.GetVendorCredentials();
        if (string.IsNullOrEmpty(vendorDetails["vendor"]) || string.IsNullOrEmpty(vendorDetails["authCode"]) || string.IsNullOrEmpty(vendorDetails["product"]) || string.IsNullOrEmpty(vendorDetails["tariff"]) || string.IsNullOrEmpty(vendorDetails["password"]))
        {
            NovalnetHelper.LogEventError(ResHelper.GetString("custom.basic_param_validation"), Order.OrderID.ToString());
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
        string uniqid = NovalnetHelper.GetUniqueid();
        var addressInfo = OrderAddressInfoProvider.GetAddressInfo(Order.OrderBillingAddress.AddressID);
        
        // Encrypted vendor parameters     
        Dictionary<string, string> vendorDetails = NovalnetHelper.GetVendorCredentials();
        string vendor = vendorDetails["vendor"];
        string password = vendorDetails["password"];
        string testMode = (SettingsKeyInfoProvider.GetValue("test_mode", SiteContext.CurrentSiteID) == "True") ? "1" : "0";
        
        int amount = NovalnetHelper.GetOrderAmount(Order.OrderGrandTotal);
        Dictionary<string, string> secureData = new Dictionary<string, string>();
        secureData.Add("authCode", NovalnetHelper.EncryptString(vendorDetails["authCode"], password, uniqid));
        secureData.Add("product", NovalnetHelper.EncryptString(vendorDetails["product"], password, uniqid));
        secureData.Add("tariff", NovalnetHelper.EncryptString(vendorDetails["tariff"], password, uniqid));
        secureData.Add("amount", NovalnetHelper.EncryptString(Convert.ToString(amount), password, uniqid));
        secureData.Add("testMode", NovalnetHelper.EncryptString(testMode, password, uniqid));
        
        string hashVal = NovalnetHelper.GetHash(secureData, uniqid, password);

        // Shop configuration parameters
        int country = addressInfo.AddressCountryID;
        CMS.Globalization.CountryInfo entity = CMS.Globalization.CountryInfoProvider.GetCountryInfo(country);
        string countryCode = entity.CountryTwoLetterCode;
        string culture = NovalnetHelper.GetLanguage(Order.OrderCulture);
        var urlBuilder = new System.UriBuilder(HttpContext.Current.Request.Url.AbsoluteUri)
        {
            Path = (HttpContext.Current.Handler as Page).ResolveUrl("~/NovalnetResponseHandler.ashx"),
            Query = null,
        };
        string url = urlBuilder.ToString();
        string sessionVal = NovalnetHelper.GetSessionValue();

        var parameter = new StringBuilder();
        // Prepare vendor parameters
        parameter.AppendFormat("&vendor={0}", vendorDetails["vendor"]);
        parameter.AppendFormat("&auth_code={0}", NovalnetHelper.EncryptString(vendorDetails["authCode"], password, uniqid));
        parameter.AppendFormat("&product={0}", NovalnetHelper.EncryptString(vendorDetails["product"], password, uniqid));
        parameter.AppendFormat("&tariff={0}", NovalnetHelper.EncryptString(vendorDetails["tariff"], password, uniqid));
        parameter.AppendFormat("&test_mode={0}", NovalnetHelper.EncryptString(testMode, password, uniqid));
        parameter.AppendFormat("&hash={0}", hashVal);
        parameter.AppendFormat("&uniqid={0}", uniqid);
        parameter.AppendFormat("&implementation={0}", "ENC");
        parameter.AppendFormat("&return_url={0}", url);
        parameter.AppendFormat("&error_return_url={0}", url);
        parameter.AppendFormat("&input1={0}", "reference1");
        parameter.AppendFormat("&inputval1={0}", sessionVal);
        parameter.AppendFormat("&hfooter={0}", "1");
        parameter.AppendFormat("&skip_cfm={0}", "1");
        parameter.AppendFormat("&skip_suc={0}", "1");
        parameter.AppendFormat("&thide={0}", "1");
        parameter.AppendFormat("&purl={0}", "1");
        if (!string.IsNullOrEmpty(SettingsKeyInfoProvider.GetValue("referrer_id", SiteContext.CurrentSiteID)))
        {
            parameter.AppendFormat("&referrer_id={0}", SettingsKeyInfoProvider.GetValue("referrer_id", SiteContext.CurrentSiteID));
        }

        // Prepare customer parameters
        parameter.AppendFormat("&gender={0}", "u");
        parameter.AppendFormat("&first_name={0}", ShoppingCartInfoObj.Customer.CustomerFirstName);
        parameter.AppendFormat("&last_name={0}", ShoppingCartInfoObj.Customer.CustomerLastName);
        parameter.AppendFormat("&email={0}", ShoppingCartInfoObj.Customer.CustomerEmail);
        parameter.AppendFormat("&customer_no={0}", mOrder.OrderCustomerID.ToString());
        if (!string.IsNullOrEmpty(addressInfo.AddressLine1)
            && !string.IsNullOrEmpty(addressInfo.AddressLine2))
        {
            parameter.AppendFormat("&house_no={0}", addressInfo.AddressLine1);
            parameter.AppendFormat("&street={0}", addressInfo.AddressLine2);
        }
        else
        {
            parameter.AppendFormat("&street={0}", addressInfo.AddressLine1);
            parameter.AppendFormat("&search_in_street={0}", "1");

        }
        parameter.AppendFormat("&country_code={0}", countryCode);
        parameter.AppendFormat("&city={0}", addressInfo.AddressCity);
        parameter.AppendFormat("&zip={0}", addressInfo.AddressZip);

        // Prepare orderdetails parameter 
        parameter.AppendFormat("&lang={0}", culture);        
        parameter.AppendFormat("&order_no={0}", OrderId.ToString());
        parameter.AppendFormat("&amount={0}", NovalnetHelper.EncryptString(amount.ToString(), password, uniqid));
        parameter.AppendFormat("&currency={0}", CurrencyInfoProvider.GetCurrencyInfo(Order.OrderCurrencyID).CurrencyCode);

        //Prepare paymentdetails parameter
        if ((!string.IsNullOrEmpty(SettingsKeyInfoProvider.GetValue("on_hold_limit", SiteContext.CurrentSiteID))) && (amount >= Convert.ToInt64(SettingsKeyInfoProvider.GetValue("on_hold_limit", SiteContext.CurrentSiteID))))
        {
            parameter.AppendFormat("&on_hold={0}", "1");
        }

        if (Convert.ToBoolean(SettingsKeyInfoProvider.GetValue("cc3d", SiteContext.CurrentSiteID)))
        {
            parameter.AppendFormat("&cc_3d={0}", "1");
        }

        if (!string.IsNullOrEmpty(SettingsKeyInfoProvider.GetValue("invoice_due_date", SiteContext.CurrentSiteID)))
        {
            parameter.AppendFormat("&due_date={0}", SettingsKeyInfoProvider.GetValue("invoice_due_date", SiteContext.CurrentSiteID));
        }
        if (!string.IsNullOrEmpty(SettingsKeyInfoProvider.GetValue("sepa_due_date", SiteContext.CurrentSiteID)))
        {
            parameter.AppendFormat("&sepa_due_date={0}", SettingsKeyInfoProvider.GetValue("sepa_due_date", SiteContext.CurrentSiteID));
        }

        if (!string.IsNullOrEmpty(SettingsKeyInfoProvider.GetValue("slip_expiry_date", SiteContext.CurrentSiteID)))
        {
            var now = DateTime.Now;
            var due_date = now.AddDays(Convert.ToInt32(SettingsKeyInfoProvider.GetValue("slip_expiry_date", SiteContext.CurrentSiteID))).ToString("yyyy-MM-dd");
            parameter.AppendFormat("&cashpayment_due_date={0}", due_date);
        }

        // Get payport response
        Dictionary<string, string> responseVal = NovalnetHelper.SendRequest(parameter.ToString());
        if (responseVal == null || (Convert.ToInt32(responseVal["status"]) != 100) || string.IsNullOrEmpty(responseVal["url"]))
        {
            PaymentResult.PaymentIsFailed = true;
            if (responseVal != null)
            {
                ErrorMessage = responseVal["status_desc"];
                PaymentResult.PaymentDescription = ErrorMessage;
            }
            UpdateOrderPaymentResult();
            NovalnetHelper.LogEventError(ErrorMessage, Order.OrderGUID.ToString());
            return PaymentResult;
        }

        // Customers are redirected to this URL to finish the payment after they successfully submit the payment form
        PaymentResult.PaymentApprovalUrl = responseVal["url"];
        NovalnetHelper.LogEventInfo(Order.OrderID.ToString(), "Successfully Redirected to Novalnet Payment Gateway Url");        
        
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
        // Set transaction mode (Test)
        if (testMode == "1")
        {
            PaymentResultItemInfo customItem = new PaymentResultItemInfo();
            customItem.Header = ResHelper.GetString("custom.transaction_mode");
			customItem.Name   = "PaymentMode";
			customItem.Value  = ResHelper.GetString("custom.test_mode");
			PaymentResult.SetPaymentResultItemInfo(customItem);
		} 
        
        // Get payment Key
        string paymentKey = NovalnetHelper.GetPaymentKey(resultData);
        // Set Novalnet Payment Method
        PaymentResultItemInfo NovalnetPaymentMethod = new PaymentResultItemInfo();
        NovalnetPaymentMethod.Header = ResHelper.GetString("custom.payment_method_title");
        NovalnetPaymentMethod.Name = "PaymentMethod";
        NovalnetPaymentMethod.Value = NovalnetHelper.GetPaymentMethod(resultData);
        PaymentResult.SetPaymentResultItemInfo(NovalnetPaymentMethod);

        // Handle payment response
        if (success)
        {
            if (resultData["tid_status"] == "100"
                && !Regex.IsMatch(paymentKey, "^(?:27|59)"))
            {
                if(Regex.IsMatch(paymentKey,"(?:40|41)"))
                {
                    PaymentResult.PaymentDescription = ResHelper.GetString("custom.guarantee_comment");
                }
                PaymentResult.PaymentIsCompleted = true;
            }
            else if (Regex.IsMatch(resultData["tid_status"], "^(?:75|85|86|90|91|98|99)$") || Regex.IsMatch(paymentKey, "^(?:27|59)$"))
            {
                PaymentResult.PaymentIsAuthorized = true;
                if (Regex.IsMatch(paymentKey, "^(?:27|41)$") && resultData["tid_status"] == "100")
                {
                    PaymentResultItemInfo item = new PaymentResultItemInfo();
                    item.Header = ResHelper.GetString("custom.transaction_details");
                    item.Name = "NovalnetTransactionDetails";
                    item.Value = string.Format(ResHelper.GetString("custom.duedate_title"), resultData["due_date"]) + " | " + string.Format(ResHelper.GetString("custom.account_holder"), resultData["invoice_account_holder"]) + " | " + string.Format("IBAN: {0}", resultData["invoice_iban"]) + " | " + string.Format("BIC: {0}", resultData["invoice_bic"]) + " | " + string.Format("Bank: {0} ", resultData["invoice_bankname"]) + " | " + string.Format(ResHelper.GetString("custom.amount"), resultData["amount"] + " " + resultData["currency"]) + " | " + ResHelper.GetString("custom.payment_reference_title") + " | " + string.Format(ResHelper.GetString("custom.payment_reference1"), resultData["invoice_ref"]) + " | " + string.Format(ResHelper.GetString("custom.payment_reference2"), resultData["tid"]);
                    // Saves the custom item into the PaymentResultInfo object processed by the gateway provider
                    PaymentResult.SetPaymentResultItemInfo(item);
                }
                else if (paymentKey == "59")
                {
                    PaymentResultItemInfo ShopDetails = new PaymentResultItemInfo();
                    ShopDetails.Header = string.Format(ResHelper.GetString("custom.slipexpirydate"), resultData["cashpayment_due_date"]);
                    ShopDetails.Name = "NovalnetTransactionDetails";
                    ShopDetails.Value = ResHelper.GetString("custom.store_details") + resultData["nearest_store_title_1"] + " | " + resultData["nearest_store_street_1"] + " | " + resultData["nearest_store_city_1"] + " | " + resultData["nearest_store_zipcode_1"] + " | " + resultData["nearest_store_country_1"] + resultData["nearest_store_title_2"] + " | " + resultData["nearest_store_street_2"] + " | " + resultData["nearest_store_city_2"] + " | " + resultData["nearest_store_zipcode_2"] + " | " + resultData["nearest_store_country_2"] + resultData["nearest_store_title_3"] + " | " + resultData["nearest_store_street_3"] + " | " + resultData["nearest_store_city_3"] + " | " + resultData["nearest_store_zipcode_3"] + " | " + resultData["nearest_store_country_3"];
                    // Saves the custom item into the PaymentResultInfo object processed by the gateway provider
                    PaymentResult.SetPaymentResultItemInfo(ShopDetails);
                }
                else if (resultData["tid_status"] == "75")
                {
                    PaymentResult.PaymentDescription = paymentKey == "41" ? ResHelper.GetString("custom.guarantee_comment") + ResHelper.GetString("custom.guarantee_invoice_comment") : ResHelper.GetString("custom.guarantee_comment") + ResHelper.GetString("custom.guarantee_sepa_comment");
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

}
