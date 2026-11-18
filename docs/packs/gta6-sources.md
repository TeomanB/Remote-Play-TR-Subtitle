# GTA VI sözlük paketi: kaynaklar ve doğrulama (18 Kasım 2026)

> Uygulama notu: `Mount Kalaga National Park` ve `Leonida Penitentiary` pakette `protect` olarak durur. Önerilen Türkçe karşılıklar resmi olmadığı için kullanılmaz. Eşleştirme aksanları yok sayar; `PTT Youngin$` OCR'da `$` yerine `S` okunursa da korunur. `The Chamber`, `Keys`, `Vapid`, `Cheetah`, `Trick`, `MW`, `Hawk`, `Crest` ve `Mudslide` büyük harf duyarlıdır: yalnız altyazıda bu biçimde yazılırlarsa korunur.

## Özet
- Dosya: `gta6-pack.json`, 102 girdi (100 protect, 2 translate)
- Kategoriler: character 31, location 25, brand_media 18, business 14, vehicle 10, weapon 3, gang 1
- Kural: Bir terim ya resmi Rockstar sayfasının metninde birebir geçmeli ya da iki bağımsız, saygın yayın tarafından resmi materyale dayanarak yazılmış olmalı. Sızıntı, söylenti, fan wikisi ve datamine kullanılmadı.
- Resmi metin 18 Kasım 2026'da rockstargames.com/VI sayfalarından (ana sayfa, only-in-leonida, music, launch-gallery, media) indirildi. Sayfaların içindeki metinden otomatik kontrol yapıldı: resmi kaynak gösterilen her terim indirilen resmi metinde birebir bulunuyor.
- Güncel durum: Resmi sitede "Coming November 19, 2026", ön yükleme açık. Son resmi videolar: Launch Trailer (15 Ekim 2026), An Extended Look (27 Ağustos 2026), Trailer 2 (6 Mayıs 2025), Trailer 1 (4 Aralık 2023).

## Kullanılan kaynaklar
Resmi:
- https://www.rockstargames.com/VI (oyun özeti, sürümler)
- https://www.rockstargames.com/VI/only-in-leonida (karakter profilleri, 6 bölge sayfası, Ultimate Edition ve Vintage Vice City Pack metinleri)
- https://www.rockstargames.com/VI/music (radyo istasyonları ve sunucular)
- https://www.rockstargames.com/VI/launch-gallery ve /VI/media (resmi ekran görüntüsü açıklamaları)
- https://www.playstation.com/en-us/games/grand-theft-auto-vi/ (aynı sürüm metni, ek teyit)

Bağımsız yayınlar (yalnızca videoda görünüp resmi metinde olmayan 4 terim için, her biri 2 kaynakla):
- IGN, "99 Details from the GTA 6 Extended Look": Effluvia, Megamundo, Andrés de León
- SVG, "GTA 6's Leonida Map: Every New Location...": Effluvia, Megamundo, Andrés de León, Bayside
- GamesRadar+, Extended Look canlı blog: Bayside
- PC Gamer karakter listesi: radyo sunucuları ve resmi karakter listesi için ek teyit

## Mevcut listenin denetimi (AppSettings.DefaultGlossary)
Mevcut 9 girdinin hepsi resmi kaynakta birebir doğrulandı: Vice City, Leonida, Leonida Keys, Port Gellhorn, Grassrivers, Ambrosia, Mount Kalaga, Jason, Lucia.
- Kaldırılacak girdi yok, yanlış girdi yok. Hepsi yeni pakette de var.
- Not: Resmi tam ad "Mount Kalaga National Park". Bunu ayrıca "Mount Kalaga Milli Parkı" çevirisiyle ekledim. Eşleştirici uzun terimi önce denediği için bu çakışmaz.

## Translate girdileri (2)
- Mount Kalaga National Park → Mount Kalaga Milli Parkı
- Leonida Penitentiary → Leonida Cezaevi
Bu iki çeviri benim önerim; resmi bir Türkçe karşılık yok. İstenirse ikisi de protect yapılabilir.

## Bilerek dışarıda bıraktıklarım

### Doğrulanmadığı için
Tek bir saygın kaynak var ya da yalnızca fan sitesi var, ikinci saygın kaynak yok:
- Starfish Island (yalnız IGN)
- Lickety Split, Cortez, Chacey's, Angstipan, Sound4Sound, Manatees, Hank's Waffles (yalnız IGN ya da fan siteleri). Hank's Waffles'ı bir fan sitesi sonradan resmi videoda olmadığını yazıp geri çekti.
- Valentina, Petra Navarro, Camilo (yalnız fan wikisi ya da fan siteleri)
- Vice Beach, Rialto Islands, Admiral Causeway (yalnız fan siteleri)
- Launch Trailer'dan: Brian's Marina, Mariana County, Mendez, North Fort Law (yalnız IGN). Goose Key, Catalan Key, Shoreline Amphitheater, Key Lento (yalnız fan siteleri)
- Trailer 2'den: Brian's Boat Works & Marina (yalnız fan wikileri), Kelly County (bu çalışmada saygın kaynak taraması yapılmadı)
- Lori Heder (tam ad): Resmi metinde yalnız "his third wife, Lori" yazıyor; "Lori" pakette var.
- Araçlar Sentinel, Bobcat, Rumpo, Willard, Schafter, Burrito, Lure Predator (yalnız IGN ya da fan siteleri)
- Silahlar Moreland 850, Duke 556 (yalnız IGN)
- Love Fist, Space Monkey, Bugstars, Palm & Flute, Nine 1 Nine, Edged (yalnız IGN; çoğu altyazıda geçmez)
- "Discover Gloriana" plakası (yalnız IGN)
- 2022 sızıntısından ya da datamine'dan gelen hiçbir isim yok.

### Resmi ama sözlüğe zarar vereceği için
Eşleştirici büyük-küçük harfe bakmıyor, bu yüzden bu kelimeler normal İngilizce cümleleri bozardı:
- "The Chamber" (radyo): Normal "the chamber" ifadesi de korunurdu.
- "Keys" (tek başına "the Keys"): Normal "keys" (anahtarlar) bozulurdu. Yerine "Leonida Keys" var.
- "Vapid" (marka): Normal "vapid" (sıkıcı) bozulurdu. Yerine model adlarıyla birlikte var (Vapid Stanier vb.).
- "Cheetah" (tek başına): Normal "cheetah" (çita) bozulurdu. Yerine "Grotti Cheetah" var.
- "Trick", "MW", "Hawk", "Crest" (tek başına): Normal kelimeler. Yerine "Trick and Trina", "Crest Kayak", "Little Morgan" var.
- "Mudslide" (Brian'ın kokteyli): Normal "mudslide" (çamur kayması) bozulurdu.
- "M7" (dövme): Belirsiz, altyazıda faydası düşük.
- "Coast Guard": Google zaten "Sahil Güvenlik" diye doğru çeviriyor.

### Argo ve fragman cümleleri
Resmi alıntılardaki argo (ör. "All my dimes in this club. Meet my twin, make it a dub.") için sabit bir Türkçe karşılık eklemedim. Doğru karşılık bağlama göre değişiyor; "doğrulanmış" bir çeviri değil, benim yorumum olurdu. İsim kısmı ("Real Dimez") zaten korunuyor.

## Bilinen eşleşme sınırları
- "Andrés de León": Altyazı aksansız çıkarsa ("Andres de Leon") eşleşmez. Bu yazım biçimi kaynaklı olmadığı için eklemedim.
- "PTT Youngin$": OCR "$" yerine "S" okursa eşleşmez.
- "Afro Bank FM" ve "AfroBank FM": İkisi de resmi sayfada geçiyor, ikisi de eklendi.
