# Frontier Command — oyun iskeleti

Sıra tabanlı strateji oyunu için React/Canvas istemcisi ile .NET 10 sunucusunu bir araya getiren monorepodur. İlk oynanabilir dikey dilim; oyun oluşturma, ikinci oyuncunun katılması, dönüşümlü hex hareketi, PostgreSQL kaydı ve SignalR güncellemesini kapsar.

## Teknoloji tabanı

- Frontend: React 19, TypeScript, Vite ve HTML5 Canvas
- Backend: ASP.NET Core, .NET 10 LTS ve Clean Architecture
- Gerçek zamanlı iletişim: SignalR
- Kalıcı veri: PostgreSQL ve Entity Framework Core
- Önbellek/ölçekleme: Redis; SignalR backplane isteğe bağlıdır
- Sözleşmeler: `shared/contracts` altında JSON Schema

## Klasör yapısı

```text
.
├── frontend/                    React arayüzü ve Canvas taktik alanı
├── backend/
│   ├── Api/                     HTTP uçları, SignalR hub ve başlangıç
│   ├── Application/             Kullanım senaryoları ve portlar
│   ├── Domain/                  Oyun modeli ve kurallar
│   ├── Domain.Tests/            Saf oyun kuralı testleri
│   └── Infrastructure/          PostgreSQL ve Redis adaptörleri
├── shared/contracts/            İstemci-sunucu sözleşmeleri
└── compose.yaml                 Yerel PostgreSQL ve Redis servisleri
```

Backend bağımlılık yönü:

```text
Api -> Application <- Infrastructure
          |
          v
        Domain
```

`Domain` başka bir katmana bağlı değildir. Sunucu oyun durumunun otoritesidir; istemci yalnızca komut gönderecek, doğrulanmış ve kalıcı hale getirilmiş sonuçlar SignalR üzerinden yayınlanacaktır.

## Gereksinimler

- Node.js 24 LTS önerilir; Node.js 22.12+ da desteklenir
- npm veya pnpm
- .NET 10 SDK
- Docker Desktop veya uyumlu bir Docker Compose kurulumu

## Yerel çalıştırma

Önce veri servislerini başlatın:

```powershell
docker compose up -d
```

Backend'i başlatın:

```powershell
cd backend
dotnet restore
dotnet run --project Api/Game.Api.csproj
```

Development ortamında PostgreSQL şeması ilk açılışta otomatik oluşturulur. Üretim ortamına geçmeden önce bu davranış kapalı tutulmalı ve EF Core migration akışına geçilmelidir.

Frontend'i ayrı bir terminalde başlatın:

```powershell
cd frontend
npm install
npm run dev
```

Arayüz `http://localhost:5173`, API ise `http://localhost:5080` adresinde açılır. Vite geliştirme sunucusu `/api`, `/health` ve `/hubs` isteklerini API'ye yönlendirir.

## Hazır uçlar

- `GET /health`: temel API sağlık kontrolü
- `GET /api/system/status`: yapılandırılan altyapı bileşenleri
- `GET /api/matches`: PostgreSQL üzerindeki oyunların salt okunur listesi
- `POST /api/matches`: ev sahibi oyuncuyla oyun oluşturur
- `GET /api/matches/{id}`: güncel oyun görünümünü döndürür
- `POST /api/matches/{id}/players`: ikinci oyuncuyu oyuna ekler
- `POST /api/matches/{id}/moves`: sürüm kontrolüyle birim hamlesi uygular
- `/hubs/game`: SignalR bağlantısı ve oyun grupları

SignalR hub bağlantı hazır olayını gönderir, istemcileri maç gruplarına alıp çıkarır ve commit edilen her hamleden sonra `MatchUpdated` olayını yayınlar.

## İlk oyun kuralları

- Maç iki oyuncuyla başlar ve her oyuncunun tek bir birliği vardır.
- Batı oyuncusu ilk hamleyi yapar.
- Birlik tur başına bitişik, boş bir hex bölgesine ilerler.
- İstemcinin gönderdiği maç sürümü güncel değilse hamle reddedilir.
- Birliğini rakibin harita kenarına ulaştıran oyuncu kazanır.

## Yapılandırma

Yerel bağlantı bilgileri `backend/Api/appsettings.Development.json` ile `compose.yaml` arasında eşleşir. Üretimde bağlantı bilgilerini dosyaya yazmak yerine ortam değişkenleri veya secret manager kullanın:

```text
ConnectionStrings__GameDatabase
ConnectionStrings__Redis
Redis__UseSignalRBackplane
Cors__Origins__0
```

Tek backend örneğinde Redis backplane kapalıdır. Birden fazla API örneğine geçildiğinde `Redis__UseSignalRBackplane=true` ayarlanabilir.

## Sıradaki geliştirme adımları

1. `EnsureCreated` geliştirme davranışını EF Core migration akışına taşımak.
2. Misafir oyuncu oturumlarını imzalı token ile doğrulamak.
3. Oyun listesini ve oyuncu presence bilgisini Redis üzerinden canlı güncellemek.
4. JSON Schema üzerinden C# ve TypeScript tip üretimini otomatikleştirmek.
5. PostgreSQL entegrasyon ve çok istemcili API testlerini eklemek.
6. Birlik türleri, hareket puanları, çatışma ve arazi etkilerini tasarlamak.
