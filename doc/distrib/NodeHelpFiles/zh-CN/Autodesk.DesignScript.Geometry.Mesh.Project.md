## 详细
“Mesh.Project”返回输入网格上的一个点，该点是输入点在网格上给定向量方向的投影。要使节点正常工作，从输入点沿输入向量方向绘制的线应与提供的网格相交。

该示例图显示了节点如何工作的简单用例。输入点位于球形网格的上方，但不直接位于顶部。该点将投影在负“Z”轴向量的方向上。生成的点将投影到球体上并显示在输入点的正下方。这与“Mesh.Nearest”节点(使用相同的点和网格作为输入)的输出形成对比，后者中生成的点沿着穿过输入点(最近点)的“法线向量”位于网格上。“Line.ByStartAndEndPoint”用于显示投影点在网格上的“轨迹”。

## 示例文件

![Example](./Autodesk.DesignScript.Geometry.Mesh.Project_img.jpg)
