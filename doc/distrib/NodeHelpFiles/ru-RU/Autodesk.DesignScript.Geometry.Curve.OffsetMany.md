## Подробности
`Curve.OffsetMany` создает одну или несколько кривых путем смещения плоской кривой на заданное расстояние в плоскости, определенной нормалью плоскости. Если между смещенными кривыми компонента имеются зазоры, они заполняются путем удлинения смещенных кривых.

Входные данные `planeNormal` по умолчанию соответствуют нормали плоскости, содержащей кривую, однако для лучшего контроля направления смещения можно задать явную нормаль, параллельную исходной нормали кривой.

Например, если для нескольких кривых, имеющих одну плоскость, требуется согласованное направление смещения, можно использовать входное значение `planeNormal` для переопределения нормалей отдельных кривых и принудительного смещения всех кривых в одном направлении. Обращение нормали изменяет направление смещения.

В примере ниже сложная кривая смещена на отрицательное расстояние, которое применяется в противоположном направлении векторного произведения между прямым участком кривой и вектором нормали плоскости.
___
## Файл примера

![Curve.OffsetMany](./Autodesk.DesignScript.Geometry.Curve.OffsetMany_img.jpg)
