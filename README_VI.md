# Cashier POS — Nguyễn Tuấn Khôi, Nhóm 11

**Rà soát Cashier 16/10/2026:** xem [README_CHINHSUA_VI.md](README_CHINHSUA_VI.md) cho các sửa lỗi cuối, kết quả kiểm tra, bản chạy và phối hợp ảnh món. Manager được giữ nguyên so với gói trước. Các mục bên dưới mô tả lịch sử xây dựng module.

Đây là mã nguồn phân hệ **Cashier bằng C# / WinForms**, không phải chỉ là bản Word hoặc ảnh giao diện. Mã được dựng thành module độc lập vì nguồn hiện có chưa có solution C# của nhóm. Các tài liệu báo cáo cũ không bị thay đổi.

## 1. Những phần đã viết

| Phần | Cách sử dụng / xử lý |
| --- | --- |
| Màn hình bán hàng | Thực đơn và giỏ ở hai vùng riêng; tìm món có/không dấu; lọc danh mục; món tạm ngừng bán không được chọn. |
| Tùy chọn món | Size chọn một, topping chọn nhiều theo giới hạn dữ liệu; kiểm tra nhóm bắt buộc và phụ thu; thêm/sửa số lượng. |
| Giỏ hàng | Cùng món/cùng tùy chọn/cùng snapshot giá tự gộp số lượng; khác cấu hình hoặc giá giữ dòng riêng. Chọn dòng rồi tăng/giảm, sửa tùy chọn, xóa; số lượng 1–999 mỗi dòng. |
| Khuyến mãi | Nhập mã, kiểm tra hiệu lực/đơn tối thiểu; giảm theo phần trăm hoặc số tiền; chỉ một mã mỗi đơn; mã mới sai không làm mất mã cũ. Sửa giỏ bỏ mã để thu ngân áp dụng lại. |
| Thanh toán | Tiền mặt: kiểm tra tiền nhận, tính tiền thừa. Chuyển khoản: thu ngân xác nhận thủ công đã nhận đủ tiền; không kết nối ngân hàng/ví/cổng thanh toán. |
| Ghi nhận | Lưu giao dịch trước khi hiển thị hóa đơn; cùng một giao dịch không lưu hai lần; giữ giá, tùy chọn và khuyến mãi tại lúc bán. |
| Lỗi / phục hồi | Lưu bản chờ trước khi ghi đơn. Nếu lỗi sau ghi, truy vấn lại cùng mã. Khi chưa rõ kết quả, khóa giỏ và không cho thu lại tiền. Khởi động lại vẫn kiểm tra cùng giao dịch. |
| Hóa đơn | Xem, xem trước khi in, chọn máy in, in lại, xuất TXT UTF-8. In không gọi Checkout và không tạo đơn mới. |
| Lịch sử / đơn tương tự | Đọc tối đa 100 hóa đơn đã lưu trên máy demo; tìm theo mã/tên món/thu ngân, xem/in lại. Tạo giỏ mới dùng giá/tùy chọn hiện tại, không sao chép ưu đãi hoặc thanh toán cũ. Không phải lịch sử riêng theo khách. |

Năm hàm trong phân công đã hiện thực: `AddItemToCart`, `ApplyOption`, `ApplyPromotion`, `Checkout`, `PrintReceipt`. Các hàm hỗ trợ giỏ và phục hồi cũng có mã nguồn.

Không có CRUD danh mục, tạo khuyến mãi, tài khoản, mở/đóng ca, đối soát, báo cáo doanh thu, kho, CRM hay ví điện tử. Cashier chỉ **đọc** thực đơn/khuyến mãi và nhận phiên/ca hợp lệ từ phân hệ liên quan.

## 2. Chạy bản đã biên dịch trên Windows

1. Giải nén toàn bộ ZIP vào một thư mục; không mở EXE ngay bên trong ZIP.
2. Cài **.NET Desktop Runtime 8 x64** nếu máy chưa có. Chọn mục “.NET Desktop Runtime”, không chỉ “.NET Runtime”: <https://dotnet.microsoft.com/download/dotnet/8.0>.
3. Mở `Windows_Demo/Nhom11.Cashier.exe`. Giữ các DLL, JSON và thư mục `Data` cạnh EXE.
4. Kết quả mong đợi: cửa sổ “Bán hàng tại quầy”, thu ngân Nguyễn Tuấn Khôi, ca `DEMO-CA`, nhãn **CHẾ ĐỘ DỮ LIỆU MẪU**.

Bản chạy dùng thực đơn/khuyến mãi mẫu và lưu JSON thật trên máy. Nó **chưa dùng MongoDB của nhóm** và không phải hệ thống đăng nhập thật. Không dùng bản mẫu cho tiền/giao dịch thực tế.

Nếu Windows cảnh báo ứng dụng chưa ký số: bản này chưa có chứng thư ký mã. Mở source và build bằng Visual Studio nếu muốn kiểm tra thay vì chạy file nhận sẵn.

## 3. Mở source bằng Visual Studio

1. Dùng Visual Studio 2022 có workload **.NET desktop development**, hoặc .NET SDK 8 trên Windows.
2. Mở `Cashier.sln`.
3. Nhấp phải `Cashier.WinForms` → **Set as Startup Project**.
4. Build → Build Solution, sau đó nhấn F5.
5. Kết quả mong đợi: mở đúng cửa sổ Cashier, không có menu quản trị.

Không có Visual Studio: nhấp `BUILD_AND_RUN.cmd` sau khi cài .NET SDK 8. Có thể chạy lệnh:

```powershell
dotnet run --project src/Cashier.WinForms/Cashier.WinForms.csproj
```

Giao diện được dựng bằng mã `TableLayoutPanel`, `FlowLayoutPanel` và các control WinForms, không có file `.Designer.cs`. Nếu mở Designer mà không thấy bố cục mẫu, hãy chạy F5; chỉnh màu/font trong `Ui.cs`, bố cục trong `SalesForm.cs` và các file hộp thoại. Không phải lỗi thiếu file thiết kế.

Project Mongo là lớp tích hợp tùy chọn. Không cần MongoDB để chạy bản mẫu. Build toàn solution cần Internet lần đầu để tải `MongoDB.Driver` đã cố định phiên bản.

## 4. Chạy thử luồng bán hàng

1. Chọn **Caramel Macchiato**, size L, trân châu, số lượng 2 → **Thêm vào giỏ**. Kết quả: đơn giá 50.000 đ, tạm tính 100.000 đ.
2. Nhập **SALE10** → **Áp dụng**. Kết quả: giảm 10.000 đ, cần thanh toán 90.000 đ.
3. Nhập **HETHAN** → Áp dụng. Kết quả: thông báo mã hết hạn, SALE10 và 90.000 đ được giữ nguyên.
4. Chọn dòng, tăng/giảm/sửa món. Kết quả: giá cập nhật và mã được bỏ; nếu muốn giảm giá thì áp dụng lại.
5. Với ví dụ 90.000 đ, chọn Thanh toán, tiền mặt, khách đưa 100.000 đ → xác nhận. Kết quả: hóa đơn đã lưu, tiền thừa 10.000 đ.
6. Xem hóa đơn hoặc in. Nếu không có máy in, dùng **Lưu bản văn bản**. Không diễn giải việc xuất TXT là đã thử máy in thật.
7. Đóng hóa đơn rồi chọn **Đơn mới** để tiếp tục. Giỏ đã trả tiền được giữ để đối chiếu cho tới khi tạo đơn mới.

Phím tắt: F2 đơn mới, F4 tìm món, F9 thanh toán; Delete khi đang chọn dòng giỏ để xóa.

## 4a. Bản sửa sau khi test trên Windows (03/10/2026)

- F9 và nút Thanh toán trên giỏ rỗng báo “Vui lòng chọn ít nhất một món trước khi thanh toán”; không tạo giao dịch.
- Thêm cùng món/cùng tùy chọn/cùng giá gộp số lượng. Sửa thành cùng cấu hình dòng khác cũng gộp, giữ định danh dòng đang sửa. Nếu tổng vượt 999 thì báo lỗi và giữ nguyên giỏ.
- Giữ kiểm tra tiền thiếu sẵn có; bổ sung cảnh báo đỏ ghi rõ số tiền còn thiếu trong cửa sổ thanh toán.
- Ba nút đầu trang không xuống dòng; chiều cao vùng tiêu đề được tính tự động.
- Quy tắc bắt buộc/tối đa nằm ở dòng riêng trong khung tùy chọn để tránh chồng/cắt chữ.
- Đổi nhẹ sang kem/nâu/cam đất. Màu tập trung trong Ui.cs; không thêm ảnh, sidebar, Manager hoặc thay schema MongoDB.

Kiểm tra nhanh bản mới:

1. Giỏ rỗng: F9 và bấm Thanh toán đều phải báo thêm món.
2. Bấm Khoai tây chiên hai lần: một dòng, SL 2, tổng 50.000 đ.
3. Thêm cùng size/topping hai lần: một dòng cộng SL. Khác size/topping: dòng riêng.
4. Sửa một dòng thành cùng cấu hình dòng khác: gộp đúng số lượng và tổng tiền.
5. Đơn 25.000 đ, nhập 20.000 đ: cảnh báo thiếu 5.000 đ, nút xác nhận bị khóa. Nhập 30.000 đ: tiền thừa 5.000 đ, được xác nhận.
6. Kiểm tra cả ba nút đầu trang và chữ topping; thử cửa sổ thường/phóng to, tỷ lệ Windows 100%/125%.
7. Thanh toán rồi xem/in lại hóa đơn: vẫn cùng một giao dịch. Đơn mới làm sạch giỏ.

TC34–TC42 bổ sung kiểm tra gộp món, giới hạn số lượng, snapshot giá, giỏ rỗng và ưu đãi. Kết quả mới ở verification/TestResults.txt. Biên dịch và test nghiệp vụ không thay thế việc mở giao diện thật trên Windows.

## 4b. Lịch sử hóa đơn và tạo đơn tương tự

Đây là phần mở rộng luồng Cashier, không phải tính năng bắt buộc trong phân công ban đầu. Nút "Lịch sử hóa đơn" thay vị trí "Hóa đơn vừa bán" để không thêm nút gây chật hàng đầu trang.

1. Hoàn tất ít nhất một giao dịch mẫu, rồi chọn Lịch sử hóa đơn.
2. Chọn dòng để xem các món bên phải. Có thể tìm mã hóa đơn/tên món/thu ngân, xem/in lại hóa đơn gốc.
3. Chọn Tạo đơn tương tự: giỏ mới lấy sản phẩm, số lượng và lựa chọn của đơn cũ; kiểm tra lại bằng thực đơn hiện tại.
4. Giỏ đang có món chưa thanh toán phải được xác nhận trước khi thay. Chọn Không thì giữ nguyên giỏ.
5. Giá/phụ thu hiện tại được dùng; mã khuyến mãi, tiền nhận/tiền thừa, trạng thái đã trả tiền không được sao chép. Kiểm tra tổng mới rồi thanh toán như bình thường.
6. Món hoặc tùy chọn không còn hợp lệ, tổng gộp vượt 999, hoặc phiên/ca không hợp lệ: báo lỗi và không thay giỏ. Thu ngân chọn lại món thủ công hoặc dùng hóa đơn khác.
7. Xem/tạo giỏ không ghi hóa đơn hay bản chờ. Khi thanh toán mới tạo mã giao dịch mới; hóa đơn gốc giữ nguyên.
8. Đóng/mở lại ứng dụng: lịch sử vẫn đọc từ receipts/*.json. Giỏ chưa thanh toán không nằm trong lịch sử. Giao dịch đang chờ không được tạo đơn tương tự.

Kiểm tra trên Windows thêm: tìm/chọn hóa đơn hiển thị đủ món, xem/in không tạo thêm đơn, từ chối thay giỏ không mất món, tạo lại dùng tổng tiền mới và không có ưu đãi cũ, đóng/mở vẫn tra cứu được. Tệp hóa đơn lỗi được bỏ qua kèm số lượng cảnh báo; chương trình không xóa hay sửa các tệp đó.

Phạm vi: chỉ dùng JSON demo hiện có, không thêm field/collection/index, khách hàng, đăng nhập, chức năng Manager hoặc chỉnh Cashier.MongoIntegration. IReceiptHistory là interface đọc tùy chọn; adapter Mongo hiện tại không bị buộc triển khai. Khi nối DB thật, chỉ bật nút lịch sử nếu đã cấu hình nguồn đọc được nhóm duyệt và quyền xem phù hợp. Không suy diễn lịch sử chung tại quầy thành lịch sử của một khách.

TC43–TC55 kiểm tra lịch sử JSON, đơn tương tự, giá mới, không mang ưu đãi/tiền cũ, xác nhận thay giỏ, món/tùy chọn ngừng bán, giới hạn gộp, phiên/ca, giao dịch chờ và không sửa hóa đơn gốc. Các ca này không thực thi cửa sổ Windows hay máy in.

## 4c. Sửa bố cục lịch sử khi mới mở (03/10/2026)

- Hàng tiêu đề bảng tự tính chiều cao theo chữ và phần đệm, không dùng chiều cao mặc định gây cắt chữ.
- Cửa sổ mặc định rộng hơn, dành thêm chỗ cho danh sách; cột ngày/giờ và thanh toán rộng hơn. Rê chuột lên mã hóa đơn dài để xem đầy đủ, hoặc đọc trong vùng chi tiết.
- Dòng giải thích/trạng thái tự xuống dòng theo chiều rộng thực tế. Kích thước cửa sổ lúc mở được giới hạn trong vùng làm việc của màn hình sau khi co giãn DPI.
- Chỉ thay ReceiptHistoryForm và DLL WinForms tương ứng; không thay nghiệp vụ, đường dẫn lưu, dữ liệu hóa đơn, Manager hay MongoDB.

Cập nhật: đóng ứng dụng, giải nén bản mới vào thư mục riêng rồi chạy Windows_Demo/Nhom11.Cashier.exe bằng cùng tài khoản Windows. Không xóa thư mục dữ liệu tại mục 5. Hóa đơn cũ vẫn được đọc từ vị trí đó.

Kiểm tra lại trên Windows: mở Lịch sử hóa đơn và kiểm tra ngay trước khi kéo giãn cửa sổ — ba tiêu đề, ô tìm kiếm, danh sách, chi tiết và bốn nút không chồng/cắt chữ. Thử thu/phóng cửa sổ và tỷ lệ 100%/125% nếu thuận tiện. Xác nhận hóa đơn cũ vẫn có, xem/in lại và tạo đơn tương tự như trước. Bộ 55 ca nghiệp vụ/lưu trữ được chạy lại; không coi kết quả đó là kiểm thử giao diện Windows.

## 5. Vị trí dữ liệu và lỗi giao dịch

Dữ liệu mẫu lưu tại `%LOCALAPPDATA%/Nhom11/CashierDemo`:

- `receipts/*.json`: từng giao dịch đã lưu, có giá/tùy chọn/số tiền theo lúc bán.
- `pending-checkout.json`: chỉ có khi có giao dịch đang chờ. Không tự xóa/sửa tệp này khi chưa kiểm tra kết quả.
- `Windows_Demo/Data/DemoCatalog.json`: dữ liệu mẫu đọc vào giao diện. Đây là DTO nghiệp vụ của bản mẫu, **không phải schema MongoDB do nhóm xác nhận**.

Nếu xuất hiện giao dịch đang chờ:

1. Chọn **Xử lý giao dịch đang chờ** → **Kiểm tra giao dịch**.
2. Đã lưu: chương trình mở cùng hóa đơn. Không thu thêm tiền.
3. Chưa tìm thấy giao dịch: có thể **Thử ghi nhận lại**, vẫn cùng mã/nội dung, không thu lại tiền. “Chưa tìm thấy” không tự động có nghĩa là thao tác ghi từ xa đã kết thúc mà không lưu.
4. Vẫn không rõ kết quả: giữ nguyên đơn; không tạo đơn mới, không đổi nội dung hoặc xác nhận đã hoàn tất.

Nút **Mở lại giỏ chưa lưu** chỉ xuất hiện khi kho dữ liệu xác nhận có thể giải phóng giao dịch chưa lưu. Với adapter Mongo hiện tại, không cho bỏ mã giao dịch đang chờ chỉ vì một truy vấn chưa thấy đơn: thao tác ghi bị timeout có thể hoàn tất muộn. Trong trường hợp này phải giữ cùng mã để kiểm tra/thử lại; không xóa bản chờ rồi tạo mã mới.

JSON demo dùng tệp mới cho mỗi đơn, ghi tệp tạm rồi đổi tên và không ghi đè hóa đơn có nội dung khác. Đây là cơ chế kiểm thử cục bộ, không thay cho sao lưu, phân quyền hay giao dịch DB của toàn nhóm.

## 6. Các project và chỗ ghép với nhóm

| Project | Trách nhiệm |
| --- | --- |
| `Cashier.Core` | Quy tắc giỏ, giá, khuyến mãi, thanh toán, snapshot, xử lý trạng thái/ghi nhận một lần. Không có tên field/collection DB. |
| `Cashier.Infrastructure` | Đọc dữ liệu mẫu, nhận phiên thu ngân, lưu JSON và bản chờ. |
| `Cashier.WinForms` | Màn hình bán hàng, hộp thoại tùy chọn, thanh toán, hóa đơn và in. |
| `Cashier.MongoIntegration` | Driver MongoDB đọc danh mục/khuyến mãi, ghi và tìm đơn; tên collection và mapper do nhóm truyền vào. Không tạo/sửa schema hoặc index. |
| `Cashier.Tests` | Chương trình kiểm thử tự động nghiệp vụ và lưu trữ. |

**Nối với Nhi:** triển khai `ICashierSessionProvider.GetCurrentAsync()` để trả nhân viên hiện đăng nhập, quyền bán và ca đang mở; bỏ `DemoSessionProvider`. Truyền `IsDemo=false` khi đã dùng phiên thực. Không cho thu ngân tự nhập/sửa mã nhân viên, quyền hoặc mã ca. Danh mục và chính sách ưu đãi là dữ liệu đọc từ phần Nhi quản lý.

**Nối với Chi:** truyền `IMongoCollection<BsonDocument>` đúng collection được duyệt, ánh xạ `MenuItem`, `Promotion` và `IGroupOrderMapper`. Mapper phải chuyển hai chiều toàn bộ snapshot hóa đơn, gồm dòng/tùy chọn, giá, ưu đãi, tổng tiền, tiền nhận/tiền thừa, nhân viên/ca và thời điểm. `DatabaseId(OrderId)` phải ổn định, cùng giao dịch luôn cùng `_id`; không được tạo ObjectId ngẫu nhiên mỗi lần thử ghi. Nếu schema dùng ObjectId tự sinh, nhóm cần thống nhất cách lưu/tra cứu mã nghiệp vụ duy nhất trước khi nối.

Trong `Program.cs`, thay `JsonSalesDataSource`/`JsonOrderRepository` bằng adapter DB đã gắn mapper; thêm ProjectReference đến `Cashier.MongoIntegration`. Giữ `JsonCheckoutJournal` ở từng workstation. Tên `Catalog/Orders` và `SanPham/DonHang` đang khác giữa tài liệu; vì vậy mã không tự chọn một bộ tên cho DB nhóm.

Module hiện nhắm .NET 8. Nếu project gốc sau này là .NET Framework 4.x, không thể thêm reference rồi chạy ngay; cần thống nhất target hoặc chuyển mã tương thích trước khi ghép. Chưa có project gốc để xác nhận điểm này.

`MongoOrderRepository` đã có mã thao tác Driver, kiểm tra collection phải tồn tại, đọc Primary, ghi có xác nhận và journaling, không ghi đè, xử lý duplicate key. **Chưa có mapper cụ thể được Chi duyệt và chưa chạy với MongoDB nhóm**. Không có TODO giả lập bên trong luồng Cashier mẫu; phần phải thống nhất nằm ở ranh giới tích hợp.

`IOrderRepository.CanReleaseUnpaidAsync()` mặc định trả `false`. Chỉ trả `true` khi lớp lưu trữ chứng minh giao dịch chưa lưu và thao tác ghi cũ không thể hoàn tất muộn. Không dùng riêng `FindAsync()==null` để cho phép tạo một mã mới sau timeout ở DB từ xa. Adapter JSON kiểm tra lại tệp sau khi thao tác ghi cục bộ kết thúc; adapter Mongo giữ quy tắc thận trọng mặc định.

Nếu toàn hệ thống cần cập nhật quỹ ca cùng đơn bằng transaction nhiều document, nhóm phải nối thao tác đó qua `IOrderRepository` trong transaction DB. Cashier hiện không tự sửa quỹ ca của Manager. Không bật chế độ thật chỉ bằng việc đổi nhãn Demo.

## 7. Kiểm thử và trạng thái minh chứng

Chạy `RUN_TESTS.cmd` hoặc:

```powershell
dotnet run --project tests/Cashier.Tests/Cashier.Tests.csproj -c Release
```

Kết quả thực tế nằm trong `verification/TestResults.txt`; thông tin biên dịch nằm trong `verification/BuildResults.txt`. Các ca tự động kiểm tra nghiệp vụ, ghi/đọc JSON, lưu trùng, phục hồi lỗi trước/sau commit và khởi động lại. Không ghi “đã kiểm thử đạt giao diện WinForms/máy in/MongoDB nhóm” dựa trên các ca này.

Lần rà soát bản chốt bổ sung 9 ca hồi quy (TC25–TC33): lỗi đường dẫn/JSON không bị coi là tệp chưa có; giao dịch có thể ghi muộn không được bỏ mã; từ chối hóa đơn tiền lẻ/snapshot giảm giá sai/dòng trùng; chặn khởi tạo lặp; kiểm tra cấu hình trùng, tiền mặt lớn và phiên không có quyền bán. Đồng thời bỏ giới hạn tiền nhập 1 tỷ ở hộp thoại, cập nhật nhãn nhân viên/ca theo phiên mới và khóa giỏ khi không có quyền bán/ca đóng.

Đã có mã chức năng và bản biên dịch WinForms. Môi trường thực hiện là Linux; chưa chạy cửa sổ WinForms trên Windows, chưa in giấy, chưa xác nhận dữ liệu MongoDB hay đăng nhập/ca của nhóm. Vì vậy chưa thể tuyên bố toàn bộ phần tích hợp và vận hành thực tế đã hoàn tất.

Khi cập nhật báo cáo, phần “triển khai” có thể mô tả đúng các project/hàm đã viết và kết quả nghiệp vụ thực tế trong tệp kiểm thử. Ảnh giao diện chạy thật chỉ bổ sung sau khi mở chương trình trên Windows; không dùng ảnh mẫu làm minh chứng chạy.

## 8. Tài liệu kỹ thuật dùng cho triển khai

- Microsoft, Windows Forms: <https://learn.microsoft.com/dotnet/desktop/winforms/>.
- Microsoft, PrintDocument: <https://learn.microsoft.com/dotnet/api/system.drawing.printing.printdocument>.
- Microsoft, hành vi File.Exists khi gặp lỗi/quyền truy cập: <https://learn.microsoft.com/dotnet/api/system.io.file.exists>.
- Microsoft, .NET Desktop Runtime/SDK 8: <https://dotnet.microsoft.com/download/dotnet/8.0>.
- MongoDB, cấu hình đọc/ghi C# Driver: <https://www.mongodb.com/docs/drivers/csharp/current/crud/configure/>.
- MongoDB, C# Driver: <https://github.com/mongodb/mongo-csharp-driver>.
- MongoDB, kết quả và giới hạn retryable writes: <https://www.mongodb.com/docs/manual/core/retryable-writes/>.
