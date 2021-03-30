<%@ Control Language="C#" AutoEventWireup="true" CodeFile="NovalnetGatewayForm.ascx.cs" Inherits="NovalnetGatewayForm" %>
<asp:Label ID="lblError" runat="server" EnableViewState="false" CssClass="ErrorLabel" Visible="true" Font-Bold="true" Font-Size="11pt"/>
<h1 style="color:red"><asp:Label ID="testmode" EnableViewState="false" runat="server"  Visible="false" Font-Size="11pt" /></h1>
<h1>
<asp:Label ID="nnDescription" EnableViewState="false" runat="server"  Visible="true" Font-Size="11pt"/>
    </h1>
<asp:TextBox ID="txtAmount" runat="server" Visible="false" />
 <asp:Button ID="nnContinueShopping"
           Text="Continue Shopping"
           OnClick="continueShoppingEvent" 
           runat="server"
     CssClass="continue_button"
           Visible="true"/>
<script>
    jQuery(document).ready(function () {
        jQuery("input[id$='_nnContinueShopping']").css('display','none');
        if (jQuery('.ErrorLabel').html() != '') {
            jQuery("input[id$='_btnProcessPayment']").css('display', 'none');
            jQuery(".BlockContent").css('display', 'none');
            jQuery(".wrapper > h1").html("Payment Failed");
            jQuery("input[id$='_nnContinueShopping']").css('display', 'block');
            jQuery("input[id$='_nnDescription']").css('display', 'none');
        }
        else
        {
            jQuery("input[id$='_btnProcessPayment']").click();
            jQuery("input[id$='_btnProcessPayment']").css('display', 'none');
        }
    });
</script>

