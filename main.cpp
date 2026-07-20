#include <glad/glad.h>   
#include <GLFW/glfw3.h>
#include <glm/glm.hpp>
#include <glm/gtc/matrix_transform.hpp>
#include <iostream>
#include <fstream>
#include <sstream>
#include <string>

// --- 空间与摄像机状态 ---
glm::vec3 cameraPos = glm::vec3(0.0f, 0.0f, 0.0f);
glm::vec3 cameraFront = glm::vec3(0.0f, 0.0f, -1.0f);
glm::vec3 cameraUp = glm::vec3(0.0f, 1.0f, 0.0f);
glm::vec3 cameraRight = glm::vec3(1.0f, 0.0f, 0.0f);
glm::vec3 cameraVelocity = glm::vec3(0.0f, 0.0f, 0.0f);
glm::vec3 cameraAcceleration = glm::vec3(0.0f, 0.0f, 0.0f);

// --- 视角控制 ---
float yaw = -90.0f;
float pitch = 0.0f;
float lastX = 400.0f, lastY = 300.0f;
bool firstMouse = true;

// --- 计时器 ---
float deltaTime = 0.0f;
float lastFrame = 0.0f;

// --- 窗口/全屏切换状态 ---
bool isFullscreen = false;
int windowedX = 100, windowedY = 100;
int windowedWidth = 800, windowedHeight = 600;

// --- 物理引擎核心参数 ---
const float gravity = -19.6f;     // 稍微加大了重力，让下落更有打击感
const float jumpForce = 6.0f;       // 配合重力提升了跳跃初速度
float moveForce = 45.0f;      // 基础移动推力
float friction = 8.0f;       // 地面滑动摩擦阻尼
bool isGrounded = false;

void framebuffer_size_callback(GLFWwindow* window, int width, int height) {
    glViewport(0, 0, width, height);
}

void mouse_callback(GLFWwindow* window, double xposIn, double yposIn) {
    float xpos = static_cast<float>(xposIn);
    float ypos = static_cast<float>(yposIn);

    if (firstMouse) {
        lastX = xpos; lastY = ypos;
        firstMouse = false;
    }

    float xoffset = xpos - lastX;
    float yoffset = lastY - ypos;
    lastX = xpos; lastY = ypos;

    const float sensitivity = 0.1f;
    xoffset *= sensitivity;
    yoffset *= sensitivity;

    yaw += xoffset;
    pitch += yoffset;

    if (pitch > 89.0f)  pitch = 89.0f;
    if (pitch < -89.0f) pitch = -89.0f;

    glm::vec3 front;
    front.x = cos(glm::radians(yaw)) * cos(glm::radians(pitch));
    front.y = sin(glm::radians(pitch));
    front.z = sin(glm::radians(yaw)) * cos(glm::radians(pitch));

    cameraFront = glm::normalize(front);
    cameraRight = glm::normalize(glm::cross(cameraFront, glm::vec3(0.0f, 1.0f, 0.0f)));
    cameraUp = glm::normalize(glm::cross(cameraRight, cameraFront));
}

void processInput(GLFWwindow* window) {
    if (glfwGetKey(window, GLFW_KEY_ESCAPE) == GLFW_PRESS)
        glfwSetWindowShouldClose(window, true);

    // 【净化魔法 1】：修复了冲刺时推力没有被应用到的 Bug 
    float currentMoveForce = moveForce;
    if (glfwGetKey(window, GLFW_KEY_LEFT_CONTROL) == GLFW_PRESS) {
        currentMoveForce = 110.0f; // 冲啊，梦子！全速前进！
    }

    // 提取水平运动方向
    glm::vec3 forward = glm::normalize(glm::vec3(cameraFront.x, 0.0f, cameraFront.z));
    glm::vec3 right = cameraRight;

    // 清空上一帧残留的水平加速度，完全由这一帧的键盘输入决定
    cameraAcceleration.x = 0.0f;
    cameraAcceleration.z = 0.0f;

    if (glfwGetKey(window, GLFW_KEY_W) == GLFW_PRESS) cameraAcceleration += forward * currentMoveForce;
    if (glfwGetKey(window, GLFW_KEY_S) == GLFW_PRESS) cameraAcceleration -= forward * currentMoveForce;
    if (glfwGetKey(window, GLFW_KEY_A) == GLFW_PRESS) cameraAcceleration -= right * currentMoveForce;
    if (glfwGetKey(window, GLFW_KEY_D) == GLFW_PRESS) cameraAcceleration += right * currentMoveForce;

    // 跳跃逻辑
    static bool spacePressedLastFrame = false;
    bool spacePressedThisFrame = (glfwGetKey(window, GLFW_KEY_SPACE) == GLFW_PRESS);

    if (spacePressedThisFrame && !spacePressedLastFrame && isGrounded) {
        cameraVelocity.y = jumpForce;
        isGrounded = false;
    }
    spacePressedLastFrame = spacePressedThisFrame;

    // F11 全屏切换保持原样
    static bool f11PressedLastFrame = false;
    bool f11PressedThisFrame = (glfwGetKey(window, GLFW_KEY_F11) == GLFW_PRESS);

    if (f11PressedThisFrame && !f11PressedLastFrame) {
        isFullscreen = !isFullscreen;
        if (isFullscreen) {
            glfwGetWindowPos(window, &windowedX, &windowedY);
            glfwGetWindowSize(window, &windowedWidth, &windowedHeight);
            GLFWmonitor* primaryMonitor = glfwGetPrimaryMonitor();
            const GLFWvidmode* mode = glfwGetVideoMode(primaryMonitor);
            glfwSetWindowMonitor(window, primaryMonitor, 0, 0, mode->width, mode->height, mode->refreshRate);
        }
        else {
            glfwSetWindowMonitor(window, NULL, windowedX, windowedY, windowedWidth, windowedHeight, 0);
        }
    }
    f11PressedLastFrame = f11PressedThisFrame;
}

unsigned int LoadShader(const char* vertexPath, const char* fragmentPath) {
    std::string vertexCode, fragmentCode;
    std::ifstream vShaderFile, fShaderFile;
    vShaderFile.exceptions(std::ifstream::failbit | std::ifstream::badbit);
    fShaderFile.exceptions(std::ifstream::failbit | std::ifstream::badbit);
    try {
        vShaderFile.open(vertexPath); fShaderFile.open(fragmentPath);
        std::stringstream vShaderStream, fShaderStream;
        vShaderStream << vShaderFile.rdbuf(); fShaderStream << fShaderFile.rdbuf();
        vShaderFile.close(); fShaderFile.close();
        vertexCode = vShaderStream.str(); fragmentCode = fShaderStream.str();
    }
    catch (std::ifstream::failure& e) {
        std::cout << "[错误]: 着色器文件读取失败！" << std::endl;
    }
    const char* vShaderCode = vertexCode.c_str();
    const char* fShaderCode = fragmentCode.c_str();

    unsigned int vertex, fragment;
    int success;
    char infoLog[512];

    vertex = glCreateShader(GL_VERTEX_SHADER); glShaderSource(vertex, 1, &vShaderCode, NULL); glCompileShader(vertex);
    glGetShaderiv(vertex, GL_COMPILE_STATUS, &success);
    if (!success) { glGetShaderInfoLog(vertex, 512, NULL, infoLog); std::cout << "[VS错误]:\n" << infoLog << std::endl; };

    fragment = glCreateShader(GL_FRAGMENT_SHADER); glShaderSource(fragment, 1, &fShaderCode, NULL); glCompileShader(fragment);
    glGetShaderiv(fragment, GL_COMPILE_STATUS, &success);
    if (!success) { glGetShaderInfoLog(fragment, 512, NULL, infoLog); std::cout << "[FS错误]:\n" << infoLog << std::endl; };

    unsigned int ID = glCreateProgram(); glAttachShader(ID, vertex); glAttachShader(ID, fragment); glLinkProgram(ID);
    glGetProgramiv(ID, GL_LINK_STATUS, &success);
    if (!success) { glGetProgramInfoLog(ID, 512, NULL, infoLog); std::cout << "[链接错误]:\n" << infoLog << std::endl; }
    glDeleteShader(vertex); glDeleteShader(fragment);
    return ID;
}

int main() {
    if (!glfwInit()) return -1;
    glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 3);
    glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 3);
    glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);

    GLFWwindow* window = glfwCreateWindow(800, 600, "Yume Ray Tracing Volumetric Bench", NULL, NULL);
    if (!window) { glfwTerminate(); return -1; }
    glfwMakeContextCurrent(window);
    glfwSetFramebufferSizeCallback(window, framebuffer_size_callback);

    glfwSetInputMode(window, GLFW_CURSOR, GLFW_CURSOR_DISABLED);
    glfwSetCursorPosCallback(window, mouse_callback);

    if (!gladLoadGLLoader((GLADloadproc)glfwGetProcAddress)) return -1;

    unsigned int rayTraceShaderID = LoadShader("RayTrace.vs", "RayTrace.fs");

    float quadVertices[] = {
        -1.0f,  1.0f, 0.0f,  0.0f, 1.0f,
        -1.0f, -1.0f, 0.0f,  0.0f, 0.0f,
         1.0f, -1.0f, 0.0f,  1.0f, 0.0f,
         1.0f,  1.0f, 0.0f,  1.0f, 1.0f
    };
    unsigned int quadIndices[] = { 0, 1, 2, 0, 2, 3 };
    unsigned int quadVAO, quadVBO, quadEBO;
    glGenVertexArrays(1, &quadVAO); glGenBuffers(1, &quadVBO); glGenBuffers(1, &quadEBO);
    glBindVertexArray(quadVAO);
    glBindBuffer(GL_ARRAY_BUFFER, quadVBO); glBufferData(GL_ARRAY_BUFFER, sizeof(quadVertices), quadVertices, GL_STATIC_DRAW);
    glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, quadEBO); glBufferData(GL_ELEMENT_ARRAY_BUFFER, sizeof(quadIndices), quadIndices, GL_STATIC_DRAW);
    glEnableVertexAttribArray(0); glVertexAttribPointer(0, 3, GL_FLOAT, GL_FALSE, 5 * sizeof(float), (void*)0);
    glEnableVertexAttribArray(1); glVertexAttribPointer(1, 2, GL_FLOAT, GL_FALSE, 5 * sizeof(float), (void*)(3 * sizeof(float)));
    glBindVertexArray(0);

    while (!glfwWindowShouldClose(window)) {
        float currentFrame = static_cast<float>(glfwGetTime());
        deltaTime = currentFrame - lastFrame;
        lastFrame = currentFrame;

        // 限制极端情况下的最大 dt，防止窗口卡顿时产生飞天 Bug
        if (deltaTime > 0.1f) deltaTime = 0.1f;

        processInput(window);

        // 【净化魔法 2】：重力属于环境加速度，累加在 Y 轴上
        if (!isGrounded) {
            cameraAcceleration.y = gravity;
        }
        else {
            cameraAcceleration.y = 0.0f;
        }

        // 速度更新 
        cameraVelocity += cameraAcceleration * deltaTime;

        // 应用地面水平摩擦力 
        glm::vec3 horizontalVel = glm::vec3(cameraVelocity.x, 0.0f, cameraVelocity.z);
        horizontalVel -= horizontalVel * friction * deltaTime;
        cameraVelocity.x = horizontalVel.x;
        cameraVelocity.z = horizontalVel.z;

        // 位置更新
        cameraPos += cameraVelocity * deltaTime;

        // 地面碰撞检测与限位
        float eyeHeight = 1.7f;
        float floorLevel = -2.0f;
        float minCameraY = floorLevel + eyeHeight;

        if (cameraPos.y <= minCameraY) {
            cameraPos.y = minCameraY;
            cameraVelocity.y = 0.0f;
            isGrounded = true;
        }
        else {
            isGrounded = false;
        }

        // 渲染与 Uniform 提交
        glClearColor(0.0f, 0.0f, 0.0f, 1.0f);
        glClear(GL_COLOR_BUFFER_BIT);

        glUseProgram(rayTraceShaderID);

        int width, height;
        glfwGetFramebufferSize(window, &width, &height);
        float aspect = (float)width / (float)height;
        glUniform1f(glGetUniformLocation(rayTraceShaderID, "aspectRatio"), aspect);
        glUniform1f(glGetUniformLocation(rayTraceShaderID, "time"), currentFrame);

        glUniform3fv(glGetUniformLocation(rayTraceShaderID, "camPos"), 1, &cameraPos[0]);
        glUniform3fv(glGetUniformLocation(rayTraceShaderID, "camFront"), 1, &cameraFront[0]);
        glUniform3fv(glGetUniformLocation(rayTraceShaderID, "camRight"), 1, &cameraRight[0]);
        glUniform3fv(glGetUniformLocation(rayTraceShaderID, "camUp"), 1, &cameraUp[0]);

        glBindVertexArray(quadVAO);
        glDrawElements(GL_TRIANGLES, 6, GL_UNSIGNED_INT, 0);

        glfwSwapBuffers(window);
        glfwPollEvents();
    }

    glDeleteVertexArrays(1, &quadVAO); glDeleteBuffers(1, &quadVBO); glDeleteBuffers(1, &quadEBO);
    glDeleteProgram(rayTraceShaderID);
    glfwTerminate();
    return 0;
}