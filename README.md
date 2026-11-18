# Tercüman

<img src="assets/icon.png" alt="Tercüman" width="128">

Ekrandaki İngilizce altyazıyı okuyup Türkçesini oyunun üstünde gösteren bir Windows tepsi uygulaması. PS Remote Play gibi pencerelerde seçtiğin bölgeyi Windows'un kendi metin tanımasıyla (`Windows.Media.Ocr`) okur ve ücretsiz Google Çeviri ucuyla (`translate.googleapis.com`, `client=gtx`) canlı çevirir.

Sürüm **1.5.0**. Küfürler her zaman sansürsüz çevrilir; bunu kapatan bir ayar yoktur. Özel isimler ve sözlük korunur.

## İndirme

Hazır dosyalar [Releases](https://github.com/TeomanB/Remote-Play-TR-Subtitle/releases) sayfasındadır. `main` dalına her gönderimde ve elle çalıştırılan iş akışında, `.csproj` içindeki sürüme göre `v1.5.0` gibi bir sürüm oluşturulur veya güncellenir.

| Dosya | Ne işe yarar |
| --- | --- |
| `AltyaziCevirici-Setup-1.5.0.exe` | Kullanıcı hesabına kurulum. Yönetici gerekmez. Başlat menüsü ve masaüstü kısayolu (sessiz kurulumda da) ve kaldırıcı vardır. Kurulum dili Türkçedir. |
| `AltyaziCevirici-Portable-1.5.0.zip` | Kurulumsuz kopya: `AltyaziCevirici.exe` ve `KULLANIM.txt`. |

İlk açılışta SmartScreen uyarısı çıkabilir: **Ek bilgi** → **Yine de çalıştır**. Uygulama imzalı değildir.

Ayrıntılı adımlar `KULLANIM.txt` içindedir.

## İlk açılış

Program açık temalı bir kontrol merkeziyle açılır: solda menü (Önizleme, Görünüm, Sözlük, Gelişmiş) ve durum kartı, ortada seçili ekranın büyük canlı önizlemesi, sağda seçili menünün paneli. Ekran kendiliğinden kararmaz. PS Remote Play ya da Chiaki penceresi açıksa kaynak olarak o seçilir.

1. Önizlemede iki kutu vardır: **Orijinal altyazı** (oyunun İngilizce yazısının okunduğu yer, turuncu) ve **Çeviri** (Türkçe yazının çıkacağı yer, turkuaz). İkisi de sürüklenir, köşe ve kenarlardaki 8 tutamaçtan boyutlanır ve bırakınca kendiliğinden kaydolur. Seçili kutu üstte durur; diğerini seçmek için etiketine ya da başlıktaki adına tıkla. Yerini bulduğun kutuyu başlıktaki kilitle sabitle: kilitli kutu taşınmaz ve tıklamalar alttaki kutuya geçer. Tam konum ve boyut sağdaki **Alanlar** panelinde sayı olarak da düzenlenir. **Ekranda işaretle (F8)** tam ekran seçiciyi açar.
2. **Başlat** okumayı açar, **Durdur** keser. Durum kartı çalışıp çalışmadığını ve gecikmeyi, alttaki kart son çeviriyi gösterir.
3. Pencereyi kapatmak tepsiye küçültür. Tepsi simgesi turkuaz konsol ikonudur; çalışırken yeşil, durunca gri bir nokta taşır.

## Yazı tanıma paketi

Seçilen altyazı dilinin Windows OCR paketi yoksa durum kartında **OCR paketi eksik** uyarısı çıkar ve **Gelişmiş** menüsünün yanında bir nokta belirir. **Gelişmiş → OCR dil paketi → Kur** yönetici izni ister ve paketi kendisi yükler. Paket eksikken **Başlat** seni doğrudan bu sayfaya götürür. İzni vermezsen veya kurulum olmazsa program açık kalır ve ne olduğunu Türkçe söyler. Paket hemen kullanılabiliyorsa yeniden başlatmana gerek kalmaz; Windows tanımayı geciktirirse uygulamayı bir kez kapatıp açman istenir. Kurulum sihirbazındaki kutu isteğe bağlıdır ve işaretlenmezse yönetici penceresi açılmaz.

## Kısayollar

| Tuş | İş |
| --- | --- |
| **F8** | Altyazı bölgesini tam ekranda işaretle |
| **F9** | Duraklat / devam et |
| **F10** | Çıkış |
| **Ctrl+1 … Ctrl+4** | Önizleme, Görünüm, Sözlük, Gelişmiş |
| Simgeye çift tık | Kontrol merkezini göster |
| Simgeye sağ tık | Göster, başlat/durdur, bölge, duraklat, çıkış |

## Kontrol merkezi

Açık temalı, özel çizilmiş bir penceredir. Önizlemenin altında **Kaynak** (tüm ekran ya da açık bir pencere) ve **Monitör** seçimi vardır. Sol menüdeki her bölüm sağdaki paneli değiştirir:

- **Önizleme → Alanlar:** seçili kutunun X/Y konumu ve genişlik/yükseklik değerleri (seçili kaynağın görüntüsüne göre piksel), **Varsayılana döndür**, **Ekranda işaretle (F8)** ve **Bu alanı kilitle**.
- **Görünüm:** yazı tipi, boyut, yazı rengi, dış çizgi rengi ve kalınlığı, arka plan opaklığı. Önizlemedeki örnek Türkçe altyazı anında değişir; bölge seçiliyse örnek yazı birkaç saniye oyunun üstünde de görünür.
- **Sözlük:** paket ve terim araması. Genel, Argo/Küfür, Oyun terimleri ve İsimler/Yerler açık gelir. GTA 6, RDR2, The Last of Us, God of War ve Spider-Man isteğe bağlıdır. GTA 6 listesi `data/packs/gta6.json` dosyasındadır (102 doğrulanmış ad, kaynaklar `docs/packs/gta6-sources.md`). Kendi terimini İngilizce → Türkçe ekleyebilir, silebilir, JSON ile içe veya dışa aktarabilirsin. **Büyük harfli özel isimleri çevirme** cümle ortasındaki adları olduğu gibi bırakır. Küfürler her zaman sansürsüzdür.
- **Gelişmiş:** güncellemeler, OCR dil paketi durumu ve **Kur**, altyazı dili, beyaz yazı filtresi, **Windows açılınca başlat**, açılışta otomatik güncelleme denetimi ve kısayollar.

Yeni bir sürüm varsa durum kartında ve **Gelişmiş** menüsünün yanında haber verilir. **Gelişmiş → Güncelle** kurulu kopyada kurulum dosyasını indirip sessiz kurar ve programı kapatır; kurulum bitince uygulama yeniden açılır. Taşınabilir kopya sürüm sayfasını açar. **Bu sürümü atla** o sürümü bir daha sormaz. Denetim açılışta arka planda yapılır; ağ hatasında sessiz kalır.

Kutu kilitleri yalnızca program açıkken hatırlanır. **Windows açılınca başlat** kullanıcının `Run` kaydına yazılır; kaldırıcı bu kaydı siler.

Küfür karşılıkları (İngilizce → Türkçe) sözlüğe bağlı değildir ve kapatılamaz. Örnek: "What the fuck are you doing?" → "Ne yapıyorsun lan?", "Fuck!" → "Siktir!", "Holy shit" → "Hassiktir". Oyunun `f***` diye yazdığı kelimeler de açılır. Bu düzeltme başka dil çiftlerinde çalışmaz; oralarda yalnızca sözlük uygulanır.

## Ayarların yeri

`ayarlar.json` ve `hata.log` kullanıcının yazabildiği bir yerde durur.

- Klasör yazılabiliyorsa (taşınabilir kopya, ya da varsayılan kurulum `%LocalAppData%\Programs\AltyaziCevirici`) dosyalar exe'nin yanındadır.
- Klasör yazılamıyorsa (örneğin Program Files) ikisi de `%AppData%\AltyaziCevirici` altına gider: `C:\Users\<ad>\AppData\Roaming\AltyaziCevirici`.

Tek dosyalık yayında da klasör, exe'nin bulunduğu klasördür; geçici açma klasörü kullanılmaz. Kaldırıcı bu dosyaları silmez. Eski `ayarlar.json` açılır, eksik alanlar varsayılanla tamamlanır.

## Sınırlar

- Windows 10 sürüm 2004 (19041) veya daha yenisi, 64 bit. exclusive tam ekran oyun penceresi yerine Remote Play gibi normal bir pencere beklenir.
- İngilizce altyazı için İngilizce OCR paketi gerekir. Bölgeyi dar tut; geniş alan okumayı bozar ve geciktirir.
- Çeviri için internet gerekir. Google isteği keserse veya ağ giderse program kapanmaz: İngilizce metin gri gösterilir ve kısa süre sonra yeniden dener.
- Ekran görüntüsü kaydedilmez. Google'a yalnızca okunan altyazı metni gider. `hata.log` en fazla 256 KB teknik hata yazar; altyazı metni yazılmaz.
- Küfür düzeltmesi ve cümle ortası isim tahmini İngilizce → Türkçe içindir. Satırın tamamı büyük harfse isim tahmin edilmez; o adları sözlüğe ekle.
- Aynı anda tek kopya çalışır.

## Derleme

Çekirdek testleri işletim sisteminden bağımsızdır:

```bash
dotnet test
```

WinForms uygulaması `net8.0-windows` hedefler; yayımlama Windows'ta yapılır:

```bash
dotnet publish src/App/AltyaziCevirici.csproj -c Release -r win-x64 --self-contained true -o publish
```

`publish/AltyaziCevirici.exe` tek dosyadır. Kurulum dosyası [Inno Setup](https://jrsoftware.org/isinfo.php) 6 ile `installer/AltyaziCevirici.iss` üzerinden üretilir. GitHub Actions (`windows-latest`) testleri çalıştırır, yayımlar ve `AltyaziCevirici-Setup-<sürüm>.exe` dosyasını iş akışı çıktısı olarak yükler.
