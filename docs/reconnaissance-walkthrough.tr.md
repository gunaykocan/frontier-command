# Keşif sistemi — hangi kodu nereye, neden ekledik?

Bu rehber, 27 Ağustos 2026 tarihli keşif geliştirmesini anlatır. Önceden var olan savaş sisi, arazi görüş hattı ve 90 saniyelik tur sisteminin üzerine eklendi. Monoreponun yapısı korunur: React/TypeScript arayüz, .NET oyun kuralları, PostgreSQL kalıcı kayıt.

## 1. Önce oyuncunun gördüğü davranışı tanımladık

- İzci 3; piyade ve zırhlı 1 hücre uzağı görür. Orman ve tepe arkasını gizlemeye devam eder.
- Ateş açan birlik, rakibinin sonraki turunun sonuna kadar görünür. Haritada `!` rozeti taşır.
- Düşman görüşten çıkınca son görülen yerde kesik çizgili `?` kalır. Altındaki `T05`, beşinci turda görüldüğü anlamına gelir; geri sayım değildir.
- `?` bir asker değildir. Canı gösterilmez, hedef alınamaz ve hareketi engellemez. Düşman gizlice hareket edince işaret onu takip etmez.
- Eski hücre yeniden görülür ve düşman orada yoksa iz kaldırılır. Düşman başka yerde yeniden görülürse kayıt yeni gözleme taşınır.
- Bağlantı yenilenince kayıt korunur. Rövanş eski keşif kayıtlarını devralmaz.

## 2. Domain: oyunun gerçek kuralları

### Görüş mesafesi — `backend/Domain/Rules/UnitProfiles.cs`

Buradaki `UnitProfile`, bir birlik türünün can, hareket, saldırı ve görüş değerlerini toplar. Piyade ve zırhlının `VisionRange` değerini 2'den 1'e indirdik; izci 3 kaldı.

Neden burada? Birliğin ne kadar görebildiği bir oyun kuralıdır. Sadece React'teki bir sayıyı değiştirmek sunucunun gönderdiği bilgiyi değiştirmezdi. Denge ayarı yapmak istediğinde ilk bakacağın yer bu dosyadır.

### Ateş açınca görünürlük — `backend/Domain/Entities/GameUnit.cs`

```csharp
public int? RevealedUntilTurnNumber { get; private set; }

public bool IsRevealedByAttack(int turnNumber) =>
    RevealedUntilTurnNumber >= turnNumber;
```

`int?`, değerin başlangıçta boş olabileceğini belirtir. `private set`, bu bilginin dışarıdan gelişigüzel değiştirilememesini sağlar. İçerideki `RevealByAttack` metodu şu atamayı yapar:

```csharp
RevealedUntilTurnNumber = currentTurnNumber + 1;
```

Örnek: 4. turda ateş açıldıysa değer 5 olur. Birlik 4. ve 5. turda açıkta kalır; 6. turda bu özel görünürlük biter. Normal görüş içindeyse yine görünür. Her oyuncu değişimi tur numarasını bir artırdığı için `+1` kullanıyoruz.

Neden ayrı bir saniye sayacı yok? Bu kural zamana değil tur devrine bağlı. Elle bitirme ve mevcut 90 saniye dolumu aynı tur ilerletme yolunu kullandığından iki durumda da doğru sona erer.

### Görüş hesabı — `backend/Domain/Rules/BattlefieldVision.cs`

Oyuncunun normal görüş alanını hesapladıktan sonra açığa çıkmış düşmanın yalnızca bulunduğu hücresini ekliyoruz:

```csharp
visible.Add(new BattlefieldCoordinate(enemy.Column, enemy.Row));
```

Bu satır yeni bir görüş yarıçapı oluşturmaz. Örneğin H4'te ateş edilmesi, H5'teki başka bir askeri de görmeni sağlamaz.

### Keşif hafızası — `backend/Domain/Entities/EnemySighting.cs`

Yeni sınıfın görevi, bir oyuncunun bir düşmanı en son nerede gördüğünü saklamak. `ViewerPlayerId` gören oyuncuyu, `EnemyUnitId` gözlenen düşmanı belirtir. Gerçek kopyalama `Observe` içinde yapılır:

```csharp
UnitType = enemy.Type;
Column = enemy.Column;
Row = enemy.Row;
LastSeenTurnNumber = turnNumber;
```

Can ve kalan hareketi özellikle kopyalamadık. Bu bir canlı asker kaydı değil, geçmiş gözlem kaydıdır.

### Kayıt ne zaman güncellenir? — `backend/Domain/Entities/GameMatch.cs`

Yeni `UpdateReconnaissance` metodu, her oyuncu için gerçekten görülen düşmanları bulur:

```csharp
var observedEnemies = _units.Where(unit => unit.OwnerPlayerId != player.Id
    && visible.Contains(new BattlefieldCoordinate(unit.Column, unit.Row))).ToList();
```

`Where`, koşula uyan öğeleri seçer. Buradaki iki koşul: birlik rakibe ait olmalı ve bulunduğu hücre görünür olmalı. Sadece bu liste için `Observe` çağrılır. Böylece sisin içindeki gizli hareket kayda geçmez.

Metot savaş başlangıcında, geçerli hareket/saldırı öncesi ve sonrasında, ayrıca tur değişiminde çağrılır. Önceki çağrı kaybolmadan hemen önceki gözlemi korur; sonraki çağrı yeni görüşü işler. Eski hücre artık görünür ve düşman yoksa kayıt kaldırılır. Görülen bir imha da ilgili kaydı kaldırır; görülmeyen ölüm tek başına rakibin kaydını silmez.

## 3. Application: her oyuncuya farklı ve güvenli görünüm

Dosya: `backend/Application/Common/MatchSnapshot.cs`.

`MatchSnapshot`, sunucudaki maçın oyuncuya gönderilecek sürümüdür. `units` listesinde kendi birliklerin ve şu anda görebildiğin düşmanlar vardır. Yeni `lastKnownEnemies` listesi ise ayrı bir geçmiş-bilgi listesidir:

```csharp
public sealed record LastKnownEnemySnapshot(
    Guid UnitId, UnitType UnitType, int Column, int Row, int LastSeenTurnNumber);
```

`record`, bu örnekte veri taşımak için kullanılan bir C# tipidir. Kayıt yalnızca doğru `ViewerPlayerId` için gönderilir; aynı düşman canlı olarak görünüyorsa veya eski hücresi görünüyorsa hayalet işaret gönderilmez.

Neden yalnızca CSS ile saklamadık? Gizli düşmanın güncel koordinatlarını tarayıcıya gönderip çizimini gizlemek gerçek gizlilik sağlamaz. Tarayıcının ağ yanıtında bilgi okunabilir. Filtre sunucuda olduğu için görünmeyen düşmanın güncel durumu gönderilmez; yalnızca geçmişte görülmüş bilgi gönderilir.

HTTP yanıtı ve SignalR bildirimi bu aynı görünümü kullanır. İki ayrı gizlilik kuralı yazmak zorunda kalmayız.

## 4. Eski olaylar yeni keşifle açılmasın

Dosyalar: `backend/Domain/Entities/GameMatchEvent.cs`, `GameMatch.cs` içindeki `AddEvent` ve `MatchSnapshot.cs` içindeki olay filtresi.

Eskiden bir olayın koordinatı oyuncunun şimdiki görüşüne göre değerlendiriliyordu. Bu, bugün keşfettiğin hücrede dün yapılmış gizli bir hareketi geriye dönük gösterebilirdi.

Artık olay oluşurken başlangıç ve hedef hücrelerini hangi oyuncunun gördüğünü kaydediyoruz. `FromVisibleToSeats` ve `ToVisibleToSeats` bu bilgiyi tutar: 0 kimse, 1 birinci oyuncu, 2 ikinci oyuncu, 3 ikisi. Olay gösterilirken `WasFromVisibleTo` ve `WasToVisibleTo` kullanılır.

Kendi olayların ve genel tur olayları görünmeye devam eder. Tamamlanan maçta tam rapor açılır. Eski maçların olayları için geçmiş görüşü tahmin etmediğimizden, gözlem kaydı olmayan eski rakip saha olayları maç sürerken açılmaz.

## 5. Infrastructure: sayfa yenilenince bilgi kaybolmasın

- `backend/Infrastructure/Persistence/GameDbContext.cs`: `EnemySighting` sınıfını `game_enemy_sightings` tablosuna bağlar. Maç + gören oyuncu + düşman birleşimi benzersizdir; aynı gözlem için tekrar tekrar satır oluşmaz.
- `backend/Infrastructure/Repositories/PostgresGameMatchRepository.cs`: maçı okurken `.Include(match => match.EnemySightings)` ile keşif kayıtlarını da yükler.
- `backend/Infrastructure/Persistence/Migrations/20260827143946_AddReconnaissance.cs`: yeni tabloyu, saldırıyla görünürlük sütununu ve olay-görüş sütunlarını veritabanına ekler. Designer ve model snapshot dosyaları EF aracının ürettiği şema kayıtlarıdır.

Keşif tablosu canlı asker tablosuna yabancı anahtarla bağlı değildir. Aksi halde gizli bir asker öldüğünde ilişkili keşif satırının otomatik silinmesi, rakibe hiç görmediği bir ölüm hakkında bilgi verebilirdi. Maçla ilişkisi vardır; maç silinirse ona ait kayıtlar da kaldırılır.

Bu geçiş mevcut maçları silmez. Eski aktif maçlarda kayıtlar yeni gözlemlerden itibaren oluşur; geçmişte bilinmeyen gözlemleri uydurmayız.

## 6. Frontend: gerçek asker ile eski izi ayırmak

### Veri tipleri

`frontend/src/shared/types/game.ts` dosyasında `LastKnownEnemySnapshot` ve `UnitSnapshot.isRevealedByAttack` eklendi. `shared/contracts/http/match-snapshot.schema.json` aynı alanların ortak sözleşmesidir. TypeScript tipi geliştirme sırasında hatayı yakalar; tek başına ağ verisini çalışma anında doğrulamaz.

### React bağlantısı — `frontend/src/App.tsx`

```tsx
lastKnownEnemies={match?.lastKnownEnemies ?? []}
```

Bu özellik, keşif listesini harita bileşenine gönderir. `?.` maç yokken hata vermeden ilerler; `?? []` bilgi yoksa boş liste kullanır. Seçili hücrede eski iz varsa birlik türü, son görülen tur ve “Güncel konumu bilinmiyor” açıklaması gösterilir.

Hareket ve saldırı hedefleri hâlâ yalnızca `match.units` üzerinden hesaplanır. `lastKnownEnemies` bu hesaplara katılmadığı için bir `?` sahte asker gibi davranmaz.

### Harita çizimi — `frontend/src/features/game/TacticalBoard.tsx`

Gerçek askerleri çizmeden önce eski temasları ayrı bir döngüde çiziyoruz. Kesik çizgili çember ve soru işaretinin ana parçaları:

```ts
context.setLineDash([3, 3]);
context.stroke();
context.setLineDash([]);
context.fillText("?", 0, 0);
```

`[3, 3]`, üç piksel çizgi ve üç piksel boşluk demektir. Sonraki `[]` çizgi stilini sıfırlar; gerçek askerlerin yanlışlıkla kesik çizilmesini önler. İşaretlere can çubuğu çizilmez. Gerçek, ateş açmış askerlerin üzerinde ayrı bir amber `!` rozeti vardır.

Aynı hücrede farklı zamanlarda birden fazla düşman görülmüşse çizim üst üste binmez; en yakın tarihli tur etiketi kullanılır. Ayrıntı okuması da en son kaydı seçer. Klavye ile seçildiğinde haritanın erişilebilir açıklaması eski iz olduğunu söyler.

`frontend/src/App.module.css` yeni açıklama ve lejant stillerini içerir. `frontend/src/features/game/MatchEventLog.tsx`, hedefi gizli hareketi “görüş dışına çıktı”, başlangıcı gizli hareketi “bölgede görüldü” olarak anlatır.

## 7. Neyi test ettik?

`backend/Api.Tests/ReconnaissanceTests.cs` görüş mesafelerini; görülmeyen hareketin eski izi değiştirmemesini; yeni keşfin eski gizli olayı açmamasını; yeniden keşifle izi silmeyi; hayalet kaydın can içermemesini ve uzaktan hedef alınamamasını sınar. Saldırıyla görünürlüğün hem elle tur bitince hem süre dolunca sona erdiğini de sınar.

`backend/Infrastructure.Tests/GameDbContextMigrationTests.cs` tablo, sütun, benzersiz kayıt ve canlı askerle yabancı anahtar bulunmamasını kontrol eder.

`frontend/scripts/local-multiplayer-smoke.mjs` iki bağımsız oturumla gerçek PostgreSQL ve SignalR üzerinde aynı keşif sürecini yürütür. Kayıt yeniden okuma/bağlantı sonrasında korunur; doğrulanan boş konumun kaydı veritabanında da silinir. Rövanş temiz başlar.

Çalıştırma: depo kökünden `dotnet test backend/Game.sln`; `frontend` klasöründe `npm test`, `npm run build` ve çalışan yerel sunucuyla `npm run smoke:multiplayer`.

Bu değişiklik sonrası 73 backend ve 6 sayaç testi geçti; üretim derlemesi ve gerçek iki oyunculu keşif testi başarılı oldu. Tarayıcıda gerçek maçta düşman izi ve 390 piksel genişlikte görünüm de kontrol edildi.

Görsel denemeyi tekrar etmek için `frontend` klasöründe `node scripts/recon-visual-fixture.mjs` çalıştırılabilir. Bu yardımcı yeni bir test maçı açar; bir oyuncu olarak o maça katılıp hazır olursun. Rakip E3'e gelir. Sıra sende olunca kendi izcini H3'ten I3'e çek ve turu bitir: E3'te `? / T02` izi kalır, rakip D3'e gizlice geçse de eski iz onu takip etmez. Yardımcı yalnızca test maçını yönetir; üç dakika oyuncu bekledikten sonra zaman aşımına uğrar. Test maçını veritabanında bırakır.

## Birlikte okumaya nereden başlayalım?

Önce `UnitProfiles.cs` içindeki dört sayıya, sonra `GameUnit.cs` içindeki `currentTurnNumber + 1` satırına bak. Ardından `EnemySighting.Observe` metodunu oku. Bu üç küçük parçayı anladıktan sonra `GameMatch.UpdateReconnaissance` ve oyuncuya özel `MatchSnapshot` filtresi çok daha anlaşılır hale gelir.
