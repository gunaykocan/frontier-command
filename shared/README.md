# Shared Contracts

Bu klasör frontend ile backend arasındaki ortak sözleşmelerin kaynak tanımlarını barındırır.

- `contracts/http`: REST istek ve yanıt şemaları
- `contracts/events`: SignalR olay şemaları

İlk sözleşmeler:

- `contracts/http/system-status.schema.json`: API çalışma durumu
- `contracts/http/match-snapshot.schema.json`: oyuncular, birlikler ve tur dahil maç görünümü
- `contracts/events/server-ready.schema.json`: SignalR bağlantısı kuruldu olayı
- `contracts/events/match-updated.schema.json`: commit edilen maç durumunun SignalR yayını

Aynı modeli TypeScript ve C# içinde elle iki kez yazmak yerine bu JSON Schema dosyalarının kaynak kabul edilmesi, ardından iki tarafın tiplerinin üretilmesi hedeflenir. Henüz kod üretim adımı eklenmediği için mevcut C# ve TypeScript tipleri bu şemalarla elle eşlenmiştir.
