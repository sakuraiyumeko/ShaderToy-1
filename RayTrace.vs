#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec2 aTexCoords;

out vec2 TexCoords;

void main() {
    TexCoords = aTexCoords;
    // 直接输出纯 2D 裁剪空间坐标，铺满屏幕！
    gl_Position = vec4(aPos, 1.0); 
}
