# Altyazı Çevirici

<img src="assets/icon.png" alt="Altyazı Çevirici" width="128">

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

Program kenarlıksız bir pencerede açılır. Ortada seçili ekranın büyük önizlemesi vardır; kaynak seçimi onun hemen altındadır. Ekran kendiliğinden kararmaz.

1. Önizlemede iki kutu vardır: sarı **Orijinal altyazı** (okunan bölge) ve turkuaz **Çeviri** (Türkçe yazının yeri). İkisini de gövdesinden sürükleyerek taşırsın. Her kutunun 8 beyaz tutamacı vardır (4 köşe, 4 kenar); tutamaçtan tutup boyutu değiştirirsin. En küçük kenar 32 pikseldir. **Kenarlara yapış** kapalıdır, istersen açarsın. Altındaki X Y W H kutuları aynı ölçüleri sayı olarak yazar.
2. **Başlat** okumayı açar, **Durdur** keser. Yazı tipi, renk, dış çizgi ve opaklık değişince örnek cümle hem tuvalde hem gerçek katmanda güncellenir: "Bu bir örnek altyazıdır, ayarları buradan görebilirsin."
3. Pencereyi kapatmak tepsiye küçültür. Tepsi ve görev çubuğu simgesi turkuaz konsol ikonudur; çalışırken yeşil, durunca gri bir nokta taşır.

## Yazı tanıma paketi

Seçilen altyazı dilinin Windows OCR paketi yoksa pencerede bir şerit görünür: **İngilizce yazı tanıma paketi eksik — Kur**. **Kur** yönetici izni ister ve paketi kendisi yükler. İzni vermezsen veya kurulum olmazsa program açık kalır ve ne olduğunu Türkçe söyler. Paket hemen kullanılabiliyorsa yeniden başlatmana gerek kalmaz; Windows tanımayı geciktirirse uygulamayı bir kez kapatıp açman istenir. Kaynak panelindeki **Yazı tanıma paketini kur** aynı işi yapar. Kurulum sihirbazındaki kutu isteğe bağlıdır ve işaretlenmezse yönetici penceresi açılmaz.

## Kısayollar

| Tuş | İş |
| --- | --- |
| **F8** | Ana pencere (kutuları tuvalde düzenle) |
| **F9** | Duraklat / devam et |
| **F10** | Çıkış |
| Simgeye çift tık | Kontrol merkezini göster |
| Simgeye sağ tık | Göster, başlat/durdur, ana pencere, duraklat, çıkış |

## Kontrol merkezi

Koyu bir penceredir: büyük önizleme, hemen altında ekran seçimi, X Y W H alanları, **Başlat** ve sekmeler (Tuval, Görünüm, Sözlük, Gelişmiş).

Kutu tutamaçları `BoxEdit` ile denenir: sekiz yön, taşıma, en küçük boyut ve isteğe bağlı yapışma. Windows iş akışı `01-onizleme.png` ile `05-tutamaclar.png` ekran görüntülerini üretir; tutamaçlar beyaz kare olarak görünür.

- **Çeviri:** diller, isimleri koruma. Küfürler her zaman sansürsüzdür.
- **Altyazı Görünümü:** yazı boyutu, arka plan opaklığı ve dış çizgi kaydırıcıları; renk. Değişiklik altyazıda hemen görünür.
- **Sözlük:** Genel, Argo/Küfür, Oyun terimleri ve İsimler/Yerler açık gelir. GTA 6, RDR2, The Last of Us, God of War ve Spider-Man isteğe bağlıdır. GTA 6 listesi `data/packs/gta6.json` dosyasındadır (kaynaklar `docs/packs/gta6-sources.md`). Kendi satırını ekleyebilirsin.
- **Gelişmiş:** tuşlar, açılışta güncelleme denetimi, **Güncellemeleri denetle** ve yazı tanıma paketini kurma.

Yeni bir sürüm varsa üstte **Yeni sürüm var: vX.Y.Z** şeridi çıkar. **Güncelle** kurulu kopyada kurulum dosyasını indirip sessiz kurar ve programı kapatır; kurulum bitince uygulama yeniden açılır. Taşınabilir kopya sürüm sayfasını açar. **Daha sonra** şeridi gizler, **Bu sürümü atla** o sürümü bir daha sormaz. Denetim açılışta arka planda yapılır; ağ hatasında sessiz kalır.

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
