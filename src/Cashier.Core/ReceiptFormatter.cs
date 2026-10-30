using System.Text;

namespace Cashier.Core;

public static class ReceiptFormatter
{
    // Only persisted Receipt objects from Checkout/Find are passed to the UI/printer.
    public static string Format(Receipt receipt)
    {
        CartRules.ValidateReceipt(receipt);
        var text = new StringBuilder();
        text.AppendLine("POS NHÓM 11");
        text.AppendLine("HÓA ĐƠN BÁN HÀNG");
        if (receipt.IsDemo) text.AppendLine("DỮ LIỆU MẪU — KHÔNG PHẢI GIAO DỊCH THỰC TẾ");
        text.AppendLine($"Mã: {receipt.ReceiptNumber}");
        text.AppendLine($"Ngày: {receipt.PaidAt.ToLocalTime():dd/MM/yyyy HH:mm:ss}");
        text.AppendLine($"Thu ngân: {receipt.StaffName}");
        text.AppendLine($"Ca: {receipt.ShiftId}");
        text.AppendLine(new string('─', 48));
        foreach (var line in receipt.Lines)
        {
            text.AppendLine(line.ProductName);
            if (line.Options.Length > 0) text.AppendLine("  " + line.OptionText);
            text.AppendLine($"  {line.Quantity} × {CartRules.Money(line.UnitPrice)} = {CartRules.Money(line.LineTotal)}");
        }
        text.AppendLine(new string('─', 48));
        text.AppendLine($"Tạm tính: {CartRules.Money(receipt.Totals.Subtotal)}");
        text.AppendLine($"Giảm giá{(receipt.Promotion is null ? "" : " (" + receipt.Promotion.Code + ")")}: {CartRules.Money(receipt.Totals.Discount)}");
        text.AppendLine($"TỔNG THANH TOÁN: {CartRules.Money(receipt.Totals.Payable)}");
        text.AppendLine($"Phương thức: {(receipt.Payment.Method == PaymentMethod.Cash ? "Tiền mặt" : "Chuyển khoản — thu ngân xác nhận")}");
        if (receipt.Payment.Method == PaymentMethod.Cash)
        {
            text.AppendLine($"Khách đưa: {CartRules.Money(receipt.Payment.AmountReceived)}");
            text.AppendLine($"Tiền thừa: {CartRules.Money(receipt.Payment.Change)}");
        }
        text.AppendLine("Giao dịch đã ghi nhận. Cảm ơn quý khách!");
        return text.ToString();
    }
}
