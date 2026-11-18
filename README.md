# Altyazı Çevirici

Ekrandaki İngilizce altyazıyı okuyup Türkçesini oyunun üstünde gösteren bir Windows tepsi uygulaması. PS Remote Play gibi pencerelerde seçtiğin bölgeyi Windows'un kendi metin tanımasıyla (`Windows.Media.Ocr`) okur ve ücretsiz Google Çeviri ucuyla (`translate.googleapis.com`, `client=gtx`) canlı çevirir.

Sürüm **1.2.0**. Küfürler her zaman sansürsüz çevrilir; bunu kapatan bir ayar yoktur. Özel isimler ve sözlük korunur.

## İndirme

Hazır dosyalar [Releases](https://github.com/TeomanB/Remote-Play-TR-Subtitle/releases) sayfasındadır. `main` dalına her gönderimde ve elle çalıştırılan iş akışında, `.csproj` içindeki sürüme göre `v1.2.0` gibi bir sürüm oluşturulur veya güncellenir.

| Dosya | Ne işe yarar |
| --- | --- |
| `AltyaziCevirici-Setup-1.2.0.exe` | Kullanıcı hesabına kurulum. Yönetici gerekmez. Başlat menüsü kısayolu, isteğe bağlı masaüstü kısayolu ve kaldırıcı vardır. Kurulum dili Türkçedir. |
| `AltyaziCevirici-Portable-1.2.0.zip` | Kurulumsuz kopya: `AltyaziCevirici.exe` ve `KULLANIM.txt`. |

İlk açılışta SmartScreen uyarısı çıkabilir: **Ek bilgi** → **Yine de çalıştır**. Uygulama imzalı değildir.

Ayrıntılı adımlar `KULLANIM.txt` içindedir.

## İngilizce OCR paketi

Program Windows'un İngilizce optik karakter tanıma paketini kullanır. Paket yoksa açılışta uyarı çıkar; hiç OCR dili yoksa program çalışamaz.

Yönetici PowerShell:

```powershell
Add-WindowsCapability -Online -Name "Language.OCR~~~en-US~0.0.1.0"
```

Arayüzden: **Ayarlar → Zaman ve dil → Dil ve bölge → Dil ekle → English (United States)**. "Görüntülenen dil olarak ayarla" kutusunu kaldırabilirsin. **Temel yazma** ve **Optik karakter tanıma** seçili kalsın. Kurulum bitince programı yeniden aç.

Kurulum sihirbazı bu komutu isteğe bağlı sunar. Kutuyu işaretlemezsen, ya da açılan yönetici izni penceresinde **Hayır** dersen, kurulum yine tamamlanır. Komutu daha sonra kendin çalıştırabilirsin.

## Kısayollar

| Tuş | İş |
| --- | --- |
| **F8** | Altyazı bölgesini seç veya yeniden seç |
| **F9** | Duraklat / devam et |
| **F10** | Çıkış |
| Saatin yanındaki **TR** simgesine çift tık | Ayarlar penceresi |
| Simgeye sağ tık | Ayarlar, bölge seç, duraklat, çıkış |

## Ayarlar penceresi

Değişiklikler anında uygulanır. **Kaydet** ve pencereyi kapatmak yazar, **Vazgeç** eski haline döndürür.

- **Genel:** bölge, duraklat, altyazı dili, çeviri dili, cümle ortasındaki isimleri koruma, beyaz yazı filtresi.
- **Görünüm:** yazı boyutu ve rengi, arka plan opaklığı, çevirinin yeri (bölgenin üstünde / altında / altyazının üzerinde), dikey kaydırma, önizleme.
- **Sözlük:** ekranda görünen İngilizce ad ve istediğin Türkçe karşılık. Sağ sütun boşsa kelime olduğu gibi kalır. Türkçe ekler düzeltilir (`Port Gellhorn'da`, `Lucia'yla`).
- **Kısayollar:** tuşların listesi.

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

152 test vardır. WinForms uygulaması `net8.0-windows` hedefler; yayımlama Windows'ta yapılır:

```bash
dotnet publish src/App/AltyaziCevirici.csproj -c Release -r win-x64 --self-contained true -o publish
```

`publish/AltyaziCevirici.exe` tek dosyadır. Kurulum dosyası [Inno Setup](https://jrsoftware.org/isinfo.php) 6 ile `installer/AltyaziCevirici.iss` üzerinden üretilir. GitHub Actions (`windows-latest`) testleri çalıştırır, yayımlar ve `AltyaziCevirici-Setup-<sürüm>.exe` dosyasını iş akışı çıktısı olarak yükler.
