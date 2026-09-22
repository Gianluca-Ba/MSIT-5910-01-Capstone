-- Replace with the order ID shown by the demo. This file only reads capstone data.
DECLARE @id uniqueidentifier = '00000000-0000-0000-0000-000000000000';
SELECT SourceId,OrderId,Status,CreatedAt,Receipt FROM CapstoneErp.dbo.OutboxMessage WHERE OrderId=@id;
SELECT AttemptId,OrderId,AttemptedAt,Outcome FROM CapstoneErp.dbo.DeliveryAttempt WHERE OrderId=@id;
SELECT SourceId,OrderId,Payload FROM CapstoneWms.dbo.AcceptedOrder WHERE OrderId=@id;
SELECT SourceId,OrderId,Receipt FROM CapstoneWms.dbo.AcceptanceReceipt WHERE OrderId=@id;
SELECT COUNT(*) AS AcceptedOrderCount FROM CapstoneWms.dbo.AcceptedOrder WHERE SourceId='demo' AND OrderId=@id;
SELECT COUNT(*) AS ReceiptCount FROM CapstoneWms.dbo.AcceptanceReceipt WHERE SourceId='demo' AND OrderId=@id;
