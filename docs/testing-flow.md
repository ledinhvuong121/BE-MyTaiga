# Flow Metrics — UC20–UC23

Chạy **Database/12_FlowMetricsUtcHistory.sql** sau 11. Script transaction/idempotent thêm
TaskCompletionHistory (mỗi Task một lần hoàn thành đầu tiên), metadata bắt đầu theo dõi;
cập nhật trigger Task, view và procedure Flow sang UTC. Không sửa timestamp Task cũ.
Lịch sử cố ý không có FK Task/Sprint để không mất số liệu khi Task bị xóa; vẫn có FK Project.
Sprint có lịch sử hoàn thành bị chặn xóa dù Task đã xóa. Các bước cài mới phải chạy script theo số tăng dần.

## Quyền và endpoint

Bearer JWT bắt buộc; **chỉ MANAGER trong đúng dự án** có FLOW_METRIC_VIEW theo seed/report.
Member/Admin/outsider trả 403. Project CLOSED được xem, DELETED trả 404.
Base URL: `/api/v1/projects/{projectId}/flow-metrics`.

| GET | Chỉ số |
|---|---|
| /wip?sprintId= | Tổng WIP, IN_PROGRESS và IN_REVIEW; filter Sprint tùy chọn |
| /throughput?from=2026-10-20&to=2026-10-26&sprintId= | Số Task hoàn thành theo ngày, có ngày 0 |
| /throughput/sprints?from=&to=&page=1&pageSize=20 | So sánh số hoàn thành giữa các Sprint, có Sprint 0 |
| /cycle-time?sprintId=&page=1&pageSize=20 | Cycle Time của Task đang DONE |
| /cycle-time/average?sprintId= | Cycle Time trung bình và số Task thiếu/sai timestamp |
| /work-item-age?sprintId=&minAgeDays=7&page=1&pageSize=20 | Tuổi việc đã bắt đầu nhưng chưa xong; ngưỡng phát hiện tồn đọng |

ID Sprint nếu cung cấp phải cùng dự án. page>=1, pageSize 1–100.
Throughput mặc định 7 ngày UTC gần nhất gồm hôm nay, from/to bao gồm cả ngày,
lọc kỹ thuật `[from 00:00Z, to+1 00:00Z)` và <=thời điểm hiện tại.
Khoảng tối đa 366 ngày; không cho ngày tương lai hoặc tràn timeline.
minAgeDays là số thập phân 0–36500, mặc định 0; có thể dùng 7 để tìm công việc tồn đọng >=7 ngày.

## Định nghĩa

- **WIP**: số Task có status IN_PROGRESS hoặc IN_REVIEW (không phải mọi thẻ chưa DONE).
  Đây là thống kê trạng thái, không suy từ StartedAt. TODO làm lại không cộng WIP.
- **Throughput**: đếm mỗi Task một lần tại lần đầu DONE. Mở lại/hoàn thành lại không đếm thêm;
  xóa Task không làm mất lịch sử. Sprint là Sprint tại lần hoàn thành đầu tiên.
  Task/Subtask đều là work item riêng. Đây không phải số lần transition DONE.
- **Cycle Time**: `(CompletedAt - StartedAt)/86400 giây` của Task hiện đang DONE.
  Sau làm lại, dùng lần bắt đầu đầu tiên và lần hoàn thành hiện tại. Chỉ số này có thể đổi sau rework,
  trong khi Throughput lịch sử không đổi. Timestamp thiếu, đảo thứ tự hoặc tương lai cho null;
  average bỏ những hàng đó và trả skippedInvalidTasks. Không có mẫu hợp lệ: average=null, không giả 0.
- **Work Item Age**: `(asOfUtc - StartedAt)/86400` của Task đã bắt đầu và chưa DONE,
  kể cả TODO làm lại. Loại Task chưa bắt đầu hoặc StartedAt tương lai. Sắp việc già nhất trước.
- Đơn vị ngày, làm tròn 4 chữ số trong API. View/procedure legacy giữ 2 chữ số theo kiểu SQL hiện có.

Tất cả ngày giờ trả UTC. Các GET không ghi snapshot hoặc audit. sp_CaptureFlowMetric vẫn có thể
chụp WIP/Throughput/Cycle/Age của một Sprint, CapturedAt UTC, nhưng không tự chạy qua GET.

## Response

Envelope success/message/data; 400 validation,401 token,403 quyền,404 entity/scope.

WIP data={projectId,sprintId,wip,inProgress,inReview,asOfUtc}.
Throughput data={projectId,sprintId,from,to,totalDoneTasks,points,importedBaselineCount,
historyTrackingStartedAtUtc,hasPartialHistoricalCoverage,definition,asOfUtc}.
Point={date,doneTasks,historyComplete}. So sánh Sprint data={items,total,page,pageSize,
historyTrackingStartedAtUtc,hasPartialHistoricalCoverage,asOfUtc}; item có sprintId/name/status/dates/doneTasks.
Cycle list data={items,total,page,pageSize,asOfUtc}; item có taskId,title,sprintId,startedAt,completedAt,cycleTimeDays.
Average data={projectId,sprintId,validTasks,skippedInvalidTasks,averageCycleTimeDays,asOfUtc}.
Age data={items,total,page,pageSize,minAgeDays,asOfUtc,definition}; item có taskId/title/sprintId/status/
assigneeId/startedAt/workItemAgeDays.

## Giới hạn dữ liệu trước nâng cấp

Migration chỉ nhập Task **hiện đang DONE có CompletedAt** làm baseline; không thể phục hồi Task
đã hoàn thành rồi mở lại/xóa trước lúc ghi lịch sử. Do đó khoảng trước historyTrackingStartedAtUtc
có thể thiếu dữ liệu; hasPartialHistoricalCoverage và historyComplete báo rõ điều này.
Số đếm ngày cũ là số đã biết, không khẳng định toàn bộ. Không tự đổi múi giờ của timestamp cũ;
baseline sử dụng giá trị hiện có. Timestamps mới do script 11/12 ghi UTC.

## Kiểm thử

Dùng database độc lập theo `docs/testing-projects.md`, áp dụng đủ script 10,11,12.

```powershell
python tests/flow_integration.py
```

2026-10-26: **61 kiểm tra Flow đạt**, 386 kiểm tra hồi quy các module trước cũng đạt.
Build 0 warning/error. Known-answer SQL fixtures kiểm chứng Cycle 2/4 ngày => mean 3 ngày,
age/ngưỡng phân số, WIP trạng thái, throughput từng Sprint/ngày, UTC biên nửa đêm,
history partial, baseline, reopen/recomplete/delete không đếm lặp,
SQL view/snapshot procedure UTC, quyền và data isolation, GET không ghi dữ liệu,
pagination/validation/empty/null và Project CLOSED/DELETED. Không phải benchmark tải lớn.
