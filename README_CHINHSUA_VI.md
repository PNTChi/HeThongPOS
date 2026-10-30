# Bản chỉnh giao diện Cashier Phase 4

Cập nhật sau rà soát Cashier ngày 16/10/2026, trên bản giao diện ngày 15/10/2026 đã tích hợp Manager. Hai thư mục gốc và tài liệu báo cáo được giữ nguyên. Lượt rà soát cuối chỉ sửa Cashier; toàn bộ mã nguồn và kiểm thử Manager giữ nguyên so với gói đã giao trước đó.

## Chạy bản đã biên dịch

1. Giải nén toàn bộ gói vào thư mục riêng.
2. Đóng các bản Cashier cũ, rồi mở `Windows_Demo/Nhom11.Cashier.exe` để chạy riêng phần Cashier. Bản này dùng ca và danh mục mẫu riêng như trước.
3. Dùng F4 để tìm món, F9 để thanh toán và F2 để tạo đơn mới. Không cần mở Manager để trình diễn phần Cashier.
4. Nếu nhóm cần chạy tích hợp: mở `Windows_Demo_Manager/Nhom11.Manager.exe`, dùng tài khoản mẫu `admin` / `123456`, mở ca rồi chọn **Mở Bán Hàng (POS)**. Các DLL Cashier trong thư mục này đã được cập nhật; mã nghiệp vụ Manager không thay đổi.

Máy cần .NET Desktop Runtime 8 x64. Giữ DLL và thư mục `Data` cạnh EXE. Đừng chép riêng một EXE hoặc mở trực tiếp bên trong ZIP. Dữ liệu hóa đơn cũ vẫn ở `%LOCALAPPDATA%/Nhom11/CashierDemo`; không xóa hoặc sửa tệp giao dịch chờ khi cập nhật.

## Những điểm đã chỉnh

### Rà soát Cashier trước khi nộp ngày 16/10/2026

- Tải lại được phiên sau khi ca/quyền bán được mở lại, kể cả phiên cũ đang khóa. Giỏ và mã khuyến mãi được giữ; giao dịch chờ/đã trả tiền vẫn không được sửa bằng thao tác tải lại.
- Đóng cửa sổ lúc đang tải dữ liệu sẽ hủy yêu cầu khi có thể và ngừng cập nhật giao diện sau khi đóng; không tạo control hoặc hộp lỗi muộn.
- Mỗi thư mục giao dịch chỉ cho một cửa sổ Cashier dùng tại một thời điểm, kể cả Cashier mở từ một ứng dụng chủ. Đóng cửa sổ sẽ giải phóng quyền dùng dữ liệu để mở lại, không cần tắt ứng dụng chủ.
- Khi sửa giỏ, topping cũ đã ngừng bán/bị gỡ được bỏ khỏi lựa chọn và có thông báo kiểm tra lại giá. Nếu size bắt buộc không còn bán, thu ngân phải chọn size hợp lệ trước khi lưu; không tự thay size khác.

### Các chỉnh giao diện ngày 15/10/2026

- Thông tin thu ngân/ca nằm trọn một hàng riêng; cửa sổ Cashier và các hộp thoại giới hạn theo vùng làm việc màn hình.
- Thẻ món tự chia cột theo chiều rộng, hiển thị tên, giá, trạng thái và vùng ảnh. Thêm/sửa giỏ không dựng lại toàn bộ thẻ và không làm mất vị trí cuộn thực đơn.
- Có số món tìm được, số lượng trong giỏ và hướng dẫn khi giỏ trống. Tổng tiền căn phải; tiêu đề bảng tự giãn.
- Mã khuyến mãi sai báo ngay dưới ô nhập, giữ mã hợp lệ đang áp dụng. Phím Enter vẫn áp dụng mã.
- Khung size/topping và thông báo tự xuống dòng; giữ kiểm tra bắt buộc/tối đa, giá, số lượng và nút hủy.
- Thanh toán chuyển khoản ẩn ô tiền mặt; cảnh báo thiếu tiền và xác nhận nhận chuyển khoản vẫn được giữ. Các nút/thông báo thanh toán tự giãn.
- Lịch sử hóa đơn dùng mô tả phù hợp khi mở từ Manager; giữ xem/in lại và tạo đơn tương tự.
- Theo góp ý nhóm, màn hình đối soát dùng **Tiền mặt dự kiến**, **Tiền mặt thực tế**, **Chênh lệch**, có công thức giải thích. Nút và vùng giải thích có thể xuống dòng/cuộn. Làm mới không tự thay số thực tế bằng số dự kiến khi người dùng nhập 0.

Tiền mặt dự kiến = tiền đầu ca + doanh thu thu bằng tiền mặt; không cộng chuyển khoản. Chênh lệch = thực tế − dự kiến. Các phép tính nghiệp vụ hiện có được giữ nguyên.

## Phối hợp ảnh món với Manager

Cashier chỉ đọc và hiển thị ảnh. Chọn/tải/chỉnh ảnh sản phẩm thuộc phần quản lý sản phẩm của Manager và chưa có giao diện trong gói này. Không tự gán ảnh không đúng món khi nhóm chưa cung cấp ảnh.

Hợp đồng bổ sung tùy chọn `MenuItem.ImagePath` không làm thay đổi các constructor cũ hoặc yêu cầu mọi món phải có ảnh. Ví dụ trong một món của `Data/DemoCatalog.json`:

```json
"ImagePath": "Images/ca-phe-sua.jpg"
```

Đặt ảnh tại `Data/Images/ca-phe-sua.jpg` cạnh ứng dụng đang chạy. Dùng ảnh PNG, JPG/JPEG, BMP hoặc GIF; tối đa 8 MB và 25 triệu pixel. Đường dẫn tương đối nằm trong `Data/Images`. Khi phát triển, ảnh đặt trong `src/Cashier.WinForms/Data/Images` sẽ được sao chép khi build/publish. Nếu chạy từ Manager, ảnh phải nằm trong `Windows_Demo_Manager/Data/Images`.

Thiếu ảnh, ảnh lỗi hoặc đường dẫn không hợp lệ: thẻ dùng chữ viết tắt và nhãn **Chưa có ảnh**, vẫn chọn món bình thường. Ảnh không bị giữ khóa file. Manager sửa tên/giá/trạng thái của món vẫn giữ `ImagePath` cũ. Không thay collection, index hay mapper MongoDB.

## Kiểm chứng và phạm vi

Kết quả rà soát Cashier hiện tại: **58/58 nghiệp vụ/lưu trữ, 20/20 kiểm tra ảnh và 61/61 kiểm tra giao diện đạt**. Bộ 11 kiểm thử Manager của bản trước được giữ nguyên. Toàn bộ solution build Release không có cảnh báo/lỗi; đã publish bản Cashier Windows x64 và cập nhật DLL Cashier trong gói tích hợp. Log lưu tại `verification/*_Current.txt`; ảnh dựng từ cửa sổ WinForms ở `verification/ui`. Các bài thử dùng dữ liệu tạm, không ghi vào hóa đơn của người dùng. `CashierScope_Current.txt` ghi kết quả đối chiếu mã Manager với gói trước.

Mở `Cashier.sln` trong Visual Studio để sửa mã; chọn `Manager.WinForms` làm Startup Project để kiểm tra tích hợp. Có thể dùng các tệp `BUILD_AND_RUN*.cmd` và `RUN_*TESTS.cmd`. Giao diện dựng bằng C#, không có `.Designer.cs`.

Bản này vẫn dùng lưu trữ JSON hiện có. Chưa kiểm chứng MongoDB của nhóm hoặc máy in giấy; xem/in lại không gọi thanh toán mới. Kiểm tra cửa sổ được thực hiện bằng WinForms runtime và ảnh dựng tự động; vẫn nên thao tác thử trên màn hình/DPI dùng thuyết trình.
