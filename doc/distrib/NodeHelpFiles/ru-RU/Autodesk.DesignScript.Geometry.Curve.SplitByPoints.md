## Подробности
SplitByPoints позволяет разделить входную кривую в заданных точках и возвращает список полученных сегментов. Если заданные точки не находятся на кривой, этот узел находит вдоль кривой точки, ближайшие к входным точкам, и разделяет кривую в этих полученных точках. В примере ниже сначала с помощью узла ByPoints создается NURBS-кривая, где в качестве входных элементов используется набор случайных точек. Тот же набор точек используется в качестве списка точек в узле SplitByPoints. Результат представляет собой список сегментов кривой между сгенерированными точками.
___
## Файл примера

![SplitByPoints](./Autodesk.DesignScript.Geometry.Curve.SplitByPoints_img.jpg)

