# Phân hệ Quản lý (Manager POS) — Nguyễn Ngọc Yến Nhi, Nhóm 11

**Cập nhật 15/10/2026:** xem [README_CHINHSUA_VI.md](README_CHINHSUA_VI.md) cho chỉnh sửa nhãn/bố cục đối soát và giao diện Cashier tích hợp. Các kết quả minh chứng hiện tại nằm trong `verification/*_Current.txt`.

Phân hệ **Quản lý (Manager)** được xây dựng hoàn chỉnh bằng **C# / WinForms** trên nền tảng .NET 8, hiện thực toàn bộ các chức năng và giao diện quản trị theo đúng báo cáo môn học NoSQL của Nhóm 11 (Người thực hiện: **Nguyễn Ngọc Yến Nhi - MSSV: 2001230623**).

> **Lưu ý về tổ chức mã nguồn:** Toàn bộ mã nguồn, dự án và tệp tin của Manager được xây dựng thành các module riêng biệt (`Manager.Core`, `Manager.Infrastructure`, `Manager.WinForms`, `Manager.Tests`), **hoàn toàn không làm lẫn lộn hay sửa đổi mã nghiệp vụ bán hàng Cashier** của bạn Tuấn Khôi, đồng thời tích hợp dữ liệu 2 chiều chuẩn mực.

---

## 1. Đối chiếu chi tiết với Báo cáo Môn học

Phân hệ Manager hiện thực đầy đủ các quy trình nghiệp vụ, ca sử dụng (Use Case) và bản vẽ thiết kế giao diện từ Chương 2 của báo cáo:

| Mục trong Báo cáo | Tên chức năng / Quy trình | Use Case | Giao diện tương ứng | Mô tả xử lý trong mã nguồn |
| :--- | :--- | :--- | :--- | :--- |
| **Mục 2.2.2.1 & 2.4.1** | Đăng nhập và xác thực vai trò | **UC07** | **Hình 2.13** (Giao diện đăng nhập) | Xác thực tài khoản, che mật khẩu, nhận diện vai trò Quản lý / Thu ngân, kiểm tra trạng thái hoạt động và quyền hạn. |
| **Mục 2.2.2.2 & 2.4.6** | Quản lý sản phẩm và danh mục | **UC09** | **Hình 2.19** (Giao diện quản lý sản phẩm) | Lọc danh mục, tìm kiếm tên, lọc trạng thái; thêm mới, sửa đơn giá, bật/tắt kinh doanh. Ngăn chặn giá âm và tiền lẻ. |
| **Mục 2.2.2.3 & 2.4.7** | Quản lý Modifier và tùy chọn món | **UC09** | **Hình 2.20** (Giao diện quản lý tùy chọn) | Chọn món để cấu hình nhóm tùy chọn (Size, Topping, Thêm nhân), quy định số lượng chọn Min/Max, phụ thu không âm. |
| **Mục 2.2.2.4 & 2.4.8** | Quản lý chương trình khuyến mãi | **UC10** | **Hình 2.21** (Giao diện quản lý khuyến mãi) | Tạo mã KM, chọn loại giảm (% hoặc số tiền VNĐ), đơn hàng tối thiểu, kiểm tra ngày bắt đầu/kết thúc hiệu lực. |
| **Mục 2.2.2.8 & 2.4.9** | Quản lý nhân viên và tài khoản | **UC11** | **Hình 2.22** (Giao diện quản lý nhân viên) | Thêm/sửa nhân viên, cấp tài khoản, phân quyền bán hàng (`CanSell`) và quyền quản lý ca (`CanManageShift`). |
| **Mục 2.2.2.7 & 2.4.10** | Quản lý ca làm việc và đối soát | **UC07, UC08** | **Hình 2.23** (Giao diện ca làm việc & đối soát) | Mở ca với số tiền đầu ca; tổng hợp giao dịch trong ca theo phương thức (tiền mặt/chuyển khoản); đối chiếu tiền thực tế, cảnh báo chênh lệch và xác nhận đóng ca. |
| **Mục 2.2.2.9 & 2.4.11** | Tra cứu lịch sử giao dịch | **UC12** | **Hình 2.24** (Giao diện lịch sử giao dịch) | Lọc theo thời gian, nhân viên, ca, phương thức; xem chi tiết từng dòng món, phụ thu, khuyến mãi và số tiền thanh toán thực tế. |
| **Mục 2.2.2.10 & 2.4.12** | Báo cáo và thống kê doanh thu | **UC13** | **Hình 2.25** (Giao diện báo cáo & thống kê) | 4 loại báo cáo: Doanh thu theo thời gian, theo ca, top món bán chạy và phân bổ phương thức thanh toán. Thẻ KPI hiển thị tổng quan. |

---

## 2. Kiến trúc và Cấu trúc Thư mục riêng biệt

```
NguyenTuanKhoi_Cashier_POS/
├── src/
│   ├── Manager.Core/             # [MỚI] Nghiệp vụ Quản lý, quy tắc kiểm tra, dịch vụ điều khiển
│   │   ├── Models.cs             # Thực thể Tài khoản, Ca làm việc, Đối soát, Báo cáo
│   │   ├── Rules.cs              # Quy tắc xác thực, kiểm tra dữ liệu món/KM/ca/đối soát
│   │   ├── Interfaces.cs         # Interface lưu trữ và truy vấn độc lập
│   │   └── Services.cs           # ManagerController điều phối toàn bộ nghiệp vụ
│   │
│   ├── Manager.Infrastructure/   # [MỚI] Tầng lưu trữ file JSON atomic và cầu nối tích hợp
│   │   ├── ManagerJson.cs        # Xử lý ghi atomic an toàn và đọc JSON độc lập
│   │   └── JsonManagerStores.cs  # Store cho Thực đơn, Tài khoản, Ca làm việc, Đọc hóa đơn & Cầu nối Session
│   │
│   ├── Manager.WinForms/         # [MỚI] Giao diện người dùng WinForms cho Quản lý
│   │   ├── ManagerUi.cs          # Bảng màu, typography Segoe UI, nút bấm, style DataGrid
│   │   ├── LoginForm.cs          # Màn hình đăng nhập (Hình 2.13)
│   │   ├── MainManagerForm.cs    # Cửa sổ quản trị trung tâm, điều hướng 7 tab
│   │   ├── ProductManagementPanel.cs    # Tab Quản lý sản phẩm (Hình 2.19)
│   │   ├── ModifierManagementPanel.cs   # Tab Quản lý Modifier (Hình 2.20)
│   │   ├── PromotionManagementPanel.cs  # Tab Quản lý khuyến mãi (Hình 2.21)
│   │   ├── StaffManagementPanel.cs      # Tab Quản lý nhân viên (Hình 2.22)
│   │   ├── ShiftManagementPanel.cs      # Tab Ca làm việc & đối soát (Hình 2.23)
│   │   ├── TransactionHistoryPanel.cs   # Tab Lịch sử giao dịch (Hình 2.24)
│   │   ├── ReportStatisticsPanel.cs     # Tab Báo cáo thống kê (Hình 2.25)
│   │   └── Program.cs            # Khởi chạy ứng dụng Manager
│   │
│   ├── Cashier.Core/             # [GIỮ NGUYÊN] Phân hệ Bán hàng của Tuấn Khôi
│   ├── Cashier.Infrastructure/   # [GIỮ NGUYÊN]
│   ├── Cashier.WinForms/         # [GIỮ NGUYÊN]
│   └── Cashier.MongoIntegration/ # [GIỮ NGUYÊN]
│
├── tests/
│   ├── Manager.Tests/            # [MỚI] Bộ kiểm thử tự động 11 ca kiểm thử cho Manager
│   │   └── Program.cs
│   └── Cashier.Tests/            # [GIỮ NGUYÊN] 55 ca kiểm thử của Cashier
│
├── Windows_Demo_Manager/         # [MỚI] Thư mục chứa tệp EXE chạy trực tiếp trên Windows
│   ├── Nhom11.Manager.exe
│   └── Data/DemoCatalog.json
│
├── BUILD_AND_RUN_MANAGER.cmd     # Script biên dịch và chạy Manager
├── RUN_MANAGER_TESTS.cmd         # Script chạy kiểm thử Manager
└── PUBLISH_MANAGER_WINDOWS.cmd   # Script publish bản chạy Windows
```

---

## 3. Cách chạy ứng dụng Manager trên Windows

### Cách 1: Chạy file EXE có sẵn (Không cần cài SDK)
1. Mở thư mục `Windows_Demo_Manager`.
2. Chạy tệp `Nhom11.Manager.exe`.
3. Đăng nhập bằng tài khoản mẫu:
   - **Tài khoản Quản lý:** `admin` / Mật khẩu: `123456` (Nguyễn Ngọc Yến Nhi)
   - **Tài khoản Thu ngân 1:** `khoi` / Mật khẩu: `123456` (Nguyễn Tuấn Khôi)
   - **Tài khoản Thu ngân 2:** `chi` / Mật khẩu: `123456` (Phạm Ngọc Thảo Chi)

### Cách 2: Chạy bằng script lệnh
Nhấp đúp chuột vào tệp:
```
BUILD_AND_RUN_MANAGER.cmd
```
Hoặc mở terminal PowerShell tại thư mục gốc và chạy:
```powershell
dotnet run --project src/Manager.WinForms/Manager.WinForms.csproj
```

### Cách 3: Mở bằng Visual Studio 2022
1. Mở file solution `Cashier.sln`.
2. Trong Solution Explorer, nhấp chuột phải vào dự án `Manager.WinForms` → Chọn **Set as Startup Project**.
3. Nhấn **F5** để khởi chạy.

---

## 4. Tích hợp 2 chiều giữa Manager và Cashier

Theo đúng thỏa thuận phối hợp nhóm tại mục 6 và trang 62 của báo cáo:
1. **Quản lý Thực đơn & Khuyến mãi:** Khi Quản lý thêm món, đổi giá hoặc tạo khuyến mãi tại giao diện Manager, dữ liệu được lưu tức thì vào `DemoCatalog.json`. Khi Cashier mở lên hoặc tải lại, toàn bộ món và mã giảm giá mới đều sẵn sàng phục vụ bán hàng.
2. **Cung cấp Phiên đăng nhập (`ICashierSessionProvider`):** Lớp `ManagerCashierSessionProvider` trong `Manager.Infrastructure` cung cấp thông tin nhân viên thực tế đang đăng nhập và ca làm việc đang mở cho Cashier với `IsDemo = false`.
3. **Mở Cashier trực tiếp từ Manager:** Trên thanh tiêu đề của `MainManagerForm`, nút **"Mở Bán Hàng (POS)"** cho phép mở ngay giao diện thu ngân của Khôi với phiên làm việc thực sự của nhân viên hiện tại.
4. **Đối soát và Báo cáo:** Manager đọc trực tiếp các tệp hóa đơn trong `%LOCALAPPDATA%/Nhom11/CashierDemo/receipts` do Cashier xuất ra để tổng hợp doanh thu, tính toán tiền két và báo cáo thống kê chính xác tuyệt đối.

---

## 5. Kết quả Kiểm thử Tự động (Manager.Tests)

Chạy kiểm thử bằng lệnh:
```powershell
dotnet run --project tests/Manager.Tests/Manager.Tests.csproj -c Release
```
Hoặc nhấp đúp vào `RUN_MANAGER_TESTS.cmd`.

**Kết quả: Đạt 11/11 ca kiểm thử (100% Passed):**
- `PASS | TC_M01 Đăng nhập: tài khoản hợp lệ, sai mật khẩu, tài khoản bị khóa`
- `PASS | TC_M02 Mở ca: bắt đầu ca thành công, chặn mở 2 ca cùng lúc, kiểm tra quyền`
- `PASS | TC_M03 Đóng ca & đối soát: tính đúng tiền thực tế, tiền lý thuyết và chênh lệch`
- `PASS | TC_M04 Quản lý món: thêm món, sửa giá, kiểm tra giá âm và trùng mã`
- `PASS | TC_M05 Quản lý Modifier: nhóm tùy chọn, giới hạn min/max, phụ thu không âm`
- `PASS | TC_M06 Quản lý Khuyến mãi: thêm mã, kiểm tra phần trăm <= 100, ngày hiệu lực`
- `PASS | TC_M07 Quản lý Nhân viên: thêm nhân viên, kiểm tra trùng mã NV và trùng username`
- `PASS | TC_M08 Tra cứu lịch sử: lọc theo thời gian, theo ca, theo phương thức`
- `PASS | TC_M09 Báo cáo doanh thu & món bán chạy & phân bổ phương thức`
- `PASS | TC_M10 Cầu nối Session: cung cấp phiên hợp lệ và đồng bộ với Cashier`
- `PASS | TC_M11 Lưu trữ JSON: kiểm tra ghi và đọc file thật`

