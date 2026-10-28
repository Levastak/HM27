# HM27 — ревью базы игроков

Сборка 2026-10-28, сезоны 20262027, 20252026, 20242025, 20232024, 20222023. Шкала: середина 11.0, 1 ст. откл. = 3.4 ед.

## Проверка расчёта

- xG: 495691 бросков, AUC 0.7497, лог-лосс 0.2216, средний xG 0.0674 при доле голов 0.0675.
- RAPM: 1931509 стинтов 5 на 5, лямбда 64000, преимущество площадки 0.13 xG/60.
- Распределение уровня у регулярных полевых: 18-20: 2.7%, 16-17: 7.3%, 13-15: 24.6%, 10-12: 42.4%, 7-9: 19.1%, 0-6: 4.0%.
- Надёжность метрик (корреляция половинок сезона → константа сжатия k): fin r=0.253 k=598.3, pts60 r=0.904 k=250.0, lng r=0.442 k=133.4, lng_fin r=0.069 k=1421.2, net r=0.94 k=250.0, pas r=0.824 k=301.2, fo r=0.772 k=198.6, tka r=0.745 k=484.8, blk r=0.938 k=250.0, hit r=0.98 k=250.0, dec_tkgv r=0.791 k=21.2, dec_sel r=0.94 k=13.0, dis r=0.737 k=503.9, temper r=0.837 k=275.9.

## Эталонные игроки

| Игрок | Уровень | Порог | Ключевое |
| --- | --- | --- | --- |
| Connor McDavid | 19.6 | 18.5 | Ускорение 20, Передача 20, Видение 20, Командная игра 19, Большинство 19 |
| Nathan MacKinnon | 19.3 | 18.5 | Ускорение 20, Видение 19, Передача 19, Бросок издали 19, Скорость 19 |
| Nikita Kucherov | 19.0 | 18 | Передача 19, Видение 19, Хладнокровие 17, Командная игра 17, Владение шайбой 17 |
| Leon Draisaitl | 19.6 | 18 | Завершение 19, Передача 19, Большинство 18, Видение 18, Хладнокровие 18 |
| Auston Matthews | 19.0 | 17.5 | Работоспособность 20, Блокирование 18, Отбор 18, Видение 18, Владение шайбой 18 |
| Kirill Kaprizov | 19.1 | 17.5 | Выносливость 18, Видение 17, Хладнокровие 17, Владение шайбой 17, Завершение 17 |
| David Pastrnak | 19.0 | 17.5 | Передача 19, Видение 18, Завершение 17, Хладнокровие 17, Мощь броска 16 |
| Cale Makar | 18.9 | 17 | Видение 19, Передача 19, Отбор 19, Решения 18, Завершение 18 |
| Quinn Hughes | 19.5 | 17.5 | Передача 20, Владение шайбой 19, Командная игра 19, Скорость 19, Видение 19 |
| Adam Fox | 19.4 | 16.5 | Командная игра 20, Решения 19, Видение 19, Передача 19, Отбор 19 |
| Igor Shesterkin | 18.6 | 16 | Позиция 19, Реакция 19, Хладнокровие 17, Игра через заслон 16, Выносливость 15 |
| Connor Hellebuyck | 18.4 | 16 | Позиция 18, Реакция 18, Игра через заслон 17, Выносливость 17, Хладнокровие 16 |
| Andrei Vasilevskiy | 18.1 | 16 | Реакция 19, Позиция 17, Хладнокровие 17, Выносливость 16, Игра через заслон 15 |

## Топ-40 по уровню (полевые)

| # | Игрок | Поз. | Уровень | ± | Минут |
| --- | --- | --- | --- | --- | --- |
| 1 | Leon Draisaitl | C | 19.6 | 1.0 | 5787 |
| 2 | Connor McDavid | C | 19.6 | 0.9 | 6271 |
| 3 | Quinn Hughes | D | 19.5 | 0.9 | 6473 |
| 4 | Adam Fox | D | 19.4 | 1.0 | 5236 |
| 5 | Nathan MacKinnon | C | 19.3 | 0.9 | 6008 |
| 6 | Kirill Kaprizov | L | 19.1 | 1.0 | 4809 |
| 7 | Jason Robertson | L | 19.1 | 1.0 | 5360 |
| 8 | Lane Hutson | D | 19.1 | 1.1 | 4178 |
| 9 | Auston Matthews | C | 19.0 | 1.1 | 4729 |
| 10 | David Pastrnak | R | 19.0 | 1.0 | 5247 |
| 11 | Nikita Kucherov | R | 19.0 | 1.0 | 5310 |
| 12 | Cale Makar | D | 18.9 | 0.9 | 6354 |
| 13 | Evan Bouchard | D | 18.9 | 0.9 | 6825 |
| 14 | Jack Hughes | C | 18.9 | 1.1 | 4252 |
| 15 | Rasmus Dahlin | D | 18.7 | 0.9 | 6063 |
| 16 | Zach Werenski | D | 18.7 | 1.0 | 5085 |
| 17 | Brandon Hagel | L | 18.7 | 1.0 | 5016 |
| 18 | Matthew Schaefer | D | 18.6 | 1.4 | 2245 |
| 19 | Artemi Panarin | L | 18.5 | 1.0 | 5220 |
| 20 | Macklin Celebrini | C | 18.4 | 1.3 | 2953 |
| 21 | Matthew Tkachuk | L | 18.3 | 1.1 | 4088 |
| 22 | Nico Hischier | C | 18.1 | 1.0 | 4912 |
| 23 | William Nylander | R | 18.1 | 1.0 | 4849 |
| 24 | Miro Heiskanen | D | 18.1 | 0.9 | 6217 |
| 25 | Jack Eichel | C | 18.0 | 1.0 | 5521 |
| 26 | Cole Caufield | R | 18.0 | 1.1 | 4617 |
| 27 | Mitch Marner | R | 17.9 | 1.0 | 5818 |
| 28 | Matt Boldy | L | 17.7 | 1.0 | 5160 |
| 29 | Alex DeBrincat | R | 17.7 | 1.1 | 4535 |
| 30 | Sebastian Aho | C | 17.7 | 1.0 | 5606 |
| 31 | Nick Suzuki | C | 17.7 | 1.0 | 5586 |
| 32 | Brady Tkachuk | L | 17.7 | 1.1 | 4150 |
| 33 | Nikolaj Ehlers | L | 17.7 | 1.1 | 4070 |
| 34 | Robert Thomas | C | 17.6 | 1.1 | 4384 |
| 35 | Aleksander Barkov | C | 17.6 | 1.2 | 3651 |
| 36 | Jesper Bratt | L | 17.6 | 1.1 | 4788 |
| 37 | Jake Sanderson | D | 17.5 | 1.0 | 5473 |
| 38 | Josh Morrissey | D | 17.5 | 0.9 | 6012 |
| 39 | Lucas Raymond | L | 17.5 | 1.1 | 4452 |
| 40 | Mark Stone | R | 17.4 | 1.1 | 4238 |

## Топ-15 вратарей

| # | Вратарь | Уровень | ± | Бросков |
| --- | --- | --- | --- | --- |
| 1 | Igor Shesterkin | 18.6 | 1.5 | 7741 |
| 2 | Connor Hellebuyck | 18.4 | 1.5 | 8078 |
| 3 | Logan Thompson | 18.3 | 1.6 | 6706 |
| 4 | Ilya Sorokin | 18.2 | 1.5 | 7834 |
| 5 | Andrei Vasilevskiy | 18.1 | 1.5 | 7770 |
| 6 | Jeremy Swayman | 17.3 | 1.6 | 7314 |
| 7 | Jet Greaves | 16.5 | 1.8 | 3337 |
| 8 | Linus Ullmark | 15.8 | 1.6 | 6217 |
| 9 | Jake Oettinger | 15.0 | 1.5 | 8747 |
| 10 | Jakub Dobes | 15.0 | 1.8 | 3534 |
| 11 | Frederik Andersen | 14.7 | 1.8 | 4266 |
| 12 | Filip Gustavsson | 14.5 | 1.6 | 6576 |
| 13 | Jesper Wallstedt | 14.0 | 1.9 | 2598 |
| 14 | Spencer Knight | 14.0 | 1.7 | 4519 |
| 15 | Joseph Woll | 13.8 | 1.7 | 4892 |

## Характеристики: топ-12 и сюрпризы

Сюрприз — игрок с уровнем ниже 13 в топ-12 по технике. Такие строки стоит проверить глазами.

**Завершение** (tech): Leon Draisaitl 19.4, William Nylander 18.6, Morgan Geekie 18.5, Jason Robertson 18.4, Artemi Panarin 18.4, Matthew Schaefer 18.3, Anthony Mantha 18.2, Robert Thomas 18.2, Evan Bouchard 18.1, Denton Mateychuk 18.1, Tage Thompson 18.0, Cale Makar 17.9

**Мощь броска** (direct): Tage Thompson 18.7, Gustav Forsling 18.3, Michael Kesselring 18.1, Tyler Kleven 18.1, Louis Crevier 17.7, Morgan Geekie 17.6, Arber Xhekaj 17.5, Ryan Pulock 17.4, Alex Vlasic 17.3, Darren Raddysh 17.2, Colin Miller 17.2, Cale Makar 17.1

**Бросок издали** (spec): Evan Bouchard 19.4, Rasmus Dahlin 19.3, Tage Thompson 19.1, Dylan Holloway 19.0, Vince Dunn 19.0, Nathan MacKinnon 18.8, Dylan Strome 18.8, Morgan Geekie 18.7, Owen Tippett 18.7, Jack Hughes 18.7, Matthew Schaefer 18.6, Jack Eichel 18.5

**Игра у ворот** (spec): Ian Moore 19.6, Jackson LaCombe 19.5, Brady Tkachuk 19.5, Zach Hyman 19.5, Jacob MacDonald 19.3, Anders Lee 19.3, Jakob Chychrun 19.1, Nick Blankenburg 19.1, Matthew Schaefer 19.1, John Tavares 19.0, Matthew Tkachuk 18.9, Dmitri Voronkov 18.7

**Передача** (tech): Lane Hutson 19.7, Quinn Hughes 19.7, Connor McDavid 19.6, Nikita Kucherov 19.3, Cale Makar 19.2, Adam Fox 19.2, Evan Bouchard 19.1, Nathan MacKinnon 19.0, Leon Draisaitl 18.7, Mitch Marner 18.6, David Pastrnak 18.6, Macklin Celebrini 18.5

**Владение шайбой** (tech): Quinn Hughes 19.2, Brady Tkachuk 19.2, Matthew Tkachuk 19.1, Adam Fox 18.7, Nathan MacKinnon 18.6, Jordan Spence 18.6, Rasmus Dahlin 18.5, Brandon Hagel 18.3, Luke Evangelista 18.3, Arseny Gritsyuk 18.0, Zach Werenski 17.9, Lane Hutson 17.8

**Вбрасывание** (spec): Michael McLeod 18.6, Claude Giroux 17.5, Jonathan Toews 17.5, Aatu Räty 17.4, Jamie Benn 16.7, Mark Kastelic 16.3, Arttu Hyry 16.2, Jack Drury 16.2, Vincent Trocheck 16.1, J.T. Miller 16.1, John Tavares 15.9, Jean-Gabriel Pageau 15.8

**Позиционная оборона** (tech): Sam Reinhart 19.0, Seth Jarvis 18.3, Zachary Bolduc 17.7, Moritz Seider 17.6, Mark Stone 17.5, Adam Pelech 17.5, Adam Fox 17.4, Dylan Samberg 17.1, Miro Heiskanen 17.1, William Karlsson 17.1, Tyson Foerster 17.1, Jonas Brodin 17.0

**Отбор** (spec): Jaccob Slavin 19.6, K'Andre Miller 19.5, Alex Tuch 19.3, Aleksander Barkov 19.1, Adam Fox 19.0, Mathew Barzal 18.9, Mathieu Joseph 18.8, Tyler Motte 18.8, Nick Schmaltz 18.8, Jimmy Vesey 18.8, Andreas Athanasiou 18.7, Mitch Marner 18.7

**Блокирование** (spec): John Beecher 19.7, Beck Malenstyn 19.6, Ryan Poehling 19.5, Rafael Harvey-Pinard 19.3, Noel Acciari 19.2, Nick Seeler 19.1, Garnet Hathaway 18.9, Tyson Kozak 18.9, Brandon Tanev 18.9, Nathan Walker 18.6, David Savard 18.4, Michael Rasmussen 18.3

**Силовая борьба** (spec): Michael Pezzetta 19.9, Matt Rempe 19.9, Jacob Melanson 19.9, Arber Xhekaj 19.9, Lian Bichsel 19.9, Samuel Helenius 19.9, Nikita Okhotiuk 19.9, Jeremy Lauzon 19.8, Adam Klapka 19.8, Marc Gatcomb 19.7, Sammy Blais 19.6, Colton Dach 19.5

**Меньшинство** (spec): Uvis Balinskis 18.5, Sam Malinski 18.0, Thomas Harley 17.7, Jordan Spence 17.6, Sean Walker 17.1, Alexander Nikishin 17.0, Alexander Petrovic 16.8, Nolan Allan 16.8, William Lagesson 16.6, Parker Wotherspoon 16.6, Jaccob Slavin 16.5, Marc Del Gaizo 16.5

**Скорость** (direct): Beck Malenstyn 19.1, Logan Cooley 18.8, Nathan MacKinnon 18.7, Miles Wood 18.7, Quinn Hughes 18.7, Seth Jarvis 18.6, Rasmus Kupari 18.6, Connor McDavid 18.5, Jake Sanderson 18.5, Martin Necas 18.5, Denis Gurianov 18.4, Ryan Leonard 18.2

**Ускорение** (direct): Jared Wright 19.9, Connor McDavid 19.7, Tim Stützle 19.6, Nathan MacKinnon 19.6, Martin Necas 19.6, Brayden Point 19.6, Roope Hintz 19.5, Jack Eichel 19.5, Matthew Schaefer 19.4, Ryan McLeod 19.3, Denis Gurianov 19.3, Owen Tippett 19.1

**Сила** (spec): Matt Rempe 19.9, Adam Klapka 19.7, Samuel Helenius 19.3, Lian Bichsel 19.3, Elmer Soderblom 19.3, Curtis Douglas 19.3, Ross Johnston 18.9, Michael Pezzetta 18.9, Adam Edstrom 18.7, Arber Xhekaj 18.6, Mark Kastelic 18.3, Klim Kostin 18.3

**Выносливость** (spec): Connor McDavid 18.0, Quinn Hughes 17.9, Nathan MacKinnon 17.8, Zach Werenski 17.7, Kirill Kaprizov 17.6, Macklin Celebrini 17.5, Miro Heiskanen 17.3, Mikko Rantanen 17.2, Matthew Schaefer 17.2, Brock Faber 17.2, Connor Bedard 17.2, Leon Draisaitl 17.2

**Решения** (tech): Adam Fox 19.5, Cale Makar 18.3, Jaccob Slavin 18.1, Ryan O'Reilly 18.1, Zach Hyman 18.0, Jackson LaCombe 17.9, K'Andre Miller 17.9, Devon Toews 17.5, Aleksander Barkov 17.5, Gustav Forsling 17.3, Jesper Fast 17.2, Nico Hischier 17.1

**Видение** (tech): Lane Hutson 19.8, Connor McDavid 19.6, Adam Fox 19.5, Cale Makar 19.4, Matthew Tkachuk 19.3, Nathan MacKinnon 19.1, Brandon Hagel 19.1, Nikita Kucherov 19.0, Evan Bouchard 18.7, Robert Thomas 18.7, Charlie McAvoy 18.6, Quinn Hughes 18.6

**Дисциплина** (spec): Jake Sanderson 17.5, Jaccob Slavin 17.1, Alex Vlasic 16.7, Owen Power 16.6, Isac Lundeström 16.6, Jack Hughes 16.6, Matty Beniers 16.5, Philip Broberg 16.4, Emmitt Finnie 16.3, Liam Ohgren 16.3, Seth Jarvis 16.2, Cole Caufield 16.1

**Работоспособность** (spec): Auston Matthews 19.9, Elias Pettersson 19.9, Alex Tuch 19.8, Moritz Seider 19.7, Aleksander Barkov 19.5, Ryan Hartman 18.9, Alexander Wennberg 18.9, Tyler Motte 18.9, Ryan Poehling 18.8, Nick Seeler 18.7, Colton Parayko 18.7, Noel Acciari 18.5

**Командная игра** (tech): Lane Hutson 19.9, Adam Fox 19.7, Quinn Hughes 19.1, Matthew Tkachuk 19.1, Connor McDavid 19.1, Jordan Spence 19.1, Luke Evangelista 19.1, Brandon Hagel 18.9, Zach Benson 18.9, Brady Tkachuk 18.5, Aleksander Barkov 18.4, Jesper Bratt 18.4

**Большинство** (spec): Connor McDavid 18.6, Evan Bouchard 18.6, Leon Draisaitl 18.4, Ryan Nugent-Hopkins 18.3, Sam Reinhart 18.1, Aleksander Barkov 17.9, Zach Hyman 17.8, Matthew Tkachuk 17.8, Adam Fox 17.7, Nate Danielson 17.5, Miro Heiskanen 17.4, Roope Hintz 17.0

**Хладнокровие** (weak): Quinn Hughes 17.9, Connor McDavid 17.8, Leon Draisaitl 17.7, Adam Fox 17.7, Nathan MacKinnon 17.5, Jason Robertson 17.5, Jack Hughes 17.5, Lane Hutson 17.4, Kirill Kaprizov 17.3, Evan Bouchard 17.3, Auston Matthews 17.3, Cale Makar 17.2

**Стабильность** (cons): Reese Johnson 18.9, Wade Allison 18.6, Simon Benoit 18.6, Jeremy Lauzon 18.1, Luke Schenn 18.1, Carl Grundstrom 17.8, Ben Jones 17.8, Jacob Melanson 17.6, Matt Martin 17.5, Beck Malenstyn 17.3, MacKenzie Entwistle 17.3, Walker Duehr 17.2

**Вратари — Реакция**: Ilya Sorokin 19.1, Andrei Vasilevskiy 18.8, Igor Shesterkin 18.7, Connor Hellebuyck 18.2, Jet Greaves 18.1, Logan Thompson 17.2, Jeremy Swayman 17.0, Spencer Knight 15.9, Samuel Montembeault 15.3, Frederik Andersen 14.9

**Вратари — Позиция**: Logan Thompson 19.1, Igor Shesterkin 18.8, Connor Hellebuyck 18.4, Jeremy Swayman 17.6, Ilya Sorokin 17.4, Linus Ullmark 17.3, Andrei Vasilevskiy 17.3, Jet Greaves 16.6, Jakub Dobes 16.0, Filip Gustavsson 15.9

**Вратари — Контроль отскока**: Jake Oettinger 17.5, Charlie Lindgren 16.0, Joel Hofer 15.6, David Rittich 15.4, Calvin Pickard 15.2, Marc-Andre Fleury 15.2, Dan Vladar 15.0, Cam Talbot 14.9, Filip Gustavsson 14.3, Karel Vejmelka 14.2

**Вратари — Игра через заслон**: Connor Hellebuyck 17.4, Jeremy Swayman 15.8, Igor Shesterkin 15.8, Linus Ullmark 15.6, Anthony Stolarz 14.8, Jet Greaves 14.8, Samuel Montembeault 14.7, Andrei Vasilevskiy 14.7, Spencer Knight 14.6, Frederik Andersen 14.5

**Вратари — Выносливость**: Connor Hellebuyck 16.6, Andrei Vasilevskiy 16.5, Brandon Bussi 15.6, Frederik Andersen 15.5, Ilya Sorokin 15.4, Logan Thompson 15.2, Jake Allen 14.8, Igor Shesterkin 14.6, Jesper Wallstedt 14.4, Jet Greaves 14.2

**Вратари — Хладнокровие**: Andrei Vasilevskiy 16.9, Igor Shesterkin 16.6, Frederik Andersen 16.5, Connor Hellebuyck 16.4, Logan Thompson 16.3, Ilya Sorokin 16.2, Sergei Bobrovsky 15.4, Jake Oettinger 15.3, Jeremy Swayman 15.3, Jet Greaves 14.5

**Вратари — Стабильность**: Logan Thompson 15.9, Semyon Varlamov 15.8, Joseph Woll 15.6, Anthony Stolarz 15.2, Laurent Brossoit 15.2, Connor Ingram 15.2, Connor Hellebuyck 15.1, Filip Gustavsson 14.9, Colten Ellis 14.6, Spencer Knight 14.6
