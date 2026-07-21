#version 330 core
out vec4 FragColor;
in vec2 TexCoords;

uniform float aspectRatio;
uniform float time; 
uniform vec3 camPos;
uniform vec3 camFront;
uniform vec3 camRight;
uniform vec3 camUp;
uniform int uFlashlightOn;

// ================= 魔法配置区：梦子随时可以修改球的位置和半径 =================
const vec3 BALL_A_POS = vec3(0.0f, 1.5f, -5.0f);   // 1号：樱花玻璃球
const float BALL_A_R  = 0.5f;

const vec3 BALL_B_POS = vec3(3.0f, 1.0f, -6.0f);  // 2号：土豪金金属球
const float BALL_B_R  = 1.0f;

const vec3 BALL_C_POS = vec3(-2.2f, 0.8f, -4.0f); // 3号：翡翠绿塑料球
const float BALL_C_R  = 0.8f;
// =======================================================================

const float IOR_AIR = 1.0f;        
const float IOR_GLASS = 1.52f;     

struct Ray {
    vec3 origin;
    vec3 direction;
};

struct HitRecord {
    float t;
    vec3 p;
    vec3 normal;
    bool frontFace;
    int hitType; 
};

// 基础哈希噪声
float hash12(vec2 p) {
    vec3 p3  = fract(vec3(p.xyx) * 0.1031f);
    p3 += dot(p3, p3.yzx + 33.33f);
    return fract((p3.x + p3.y) * p3.z);
}

float noise(vec2 p) {
    vec2 i = floor(p); 
    vec2 f = fract(p);
    vec2 u = f * f * (3.0f - 2.0f * f);
    return mix(mix(hash12(i + vec2(0.0f, 0.0f)), hash12(i + vec2(1.0f, 0.0f)), u.x),
               mix(hash12(i + vec2(0.0f, 1.0f)), hash12(i + vec2(1.0f, 1.0f)), u.x), u.y);
}

float fbm(vec2 p) {
    float v = 0.0f; 
    float a = 0.5f;
    for (int i = 0; i < 3; i++) { 
        v += a * noise(p); 
        p *= 2.1f; 
        a *= 0.5f; 
    }
    return v;
}

vec3 calculateFlashlight(vec3 hitPoint, vec3 hitNormal, vec3 camPos, vec3 camFront) {
    if (uFlashlightOn == 0) return vec3(0.0f);

    // 计算相机指向交点的向量
    vec3 lightDir = hitPoint - camPos;
    float distance = length(lightDir);
    lightDir = normalize(lightDir);

    // 计算交点向量与相机正前方夹角的余弦值
    float theta = dot(lightDir, normalize(camFront));
    
    // 手电筒参数配置区
    float cutOff = cos(radians(12.5f));      // 内圆锥角（最亮区域）
    float outerCutOff = cos(radians(17.5f)); // 外圆锥角（边缘淡出）
    
    // 如果交点在手电筒圆锥光束之外，直接返回全黑
    if (theta < outerCutOff) return vec3(0.0f);

    // 软边缘淡出插值
    float epsilon = cutOff - outerCutOff;
    float intensity = clamp((theta - outerCutOff) / epsilon, 0.0f, 1.0f);

    // 距离衰减 (防止手电筒照到无限远)
    float attenuation = 1.0f / (1.0f + 0.07f * distance + 0.017f * distance * distance);

    // 简单的漫反射强度计算 (让照在墙面和球体上的光有角度变化)
    float diff = max(dot(hitNormal, -lightDir), 0.0f);

    // 手电筒颜色 (带有一点点温馨的黄白暖光)
    vec3 flashlightColor = vec3(1.0f, 0.95f, 0.85f) * 2.5f; 

    return flashlightColor * diff * attenuation * intensity;
}

// 射线与单球求交
bool checkSingleSphere(Ray ray, vec3 center, float radius, float tMax, out HitRecord rec) {
    vec3 oc = ray.origin - center;
    float b = dot(oc, ray.direction);
    float c = dot(oc, oc) - radius * radius;
    float h = b * b - c;

    if (h < 0.0f) return false;
    
    float sqrtd = sqrt(h);
    float root = -b - sqrtd;
    if (root < 0.001f || root > tMax) {
        root = -b + sqrtd;
        if (root < 0.001f || root > tMax) return false;
    }

    rec.t = root;
    rec.p = ray.origin + root * ray.direction;
    vec3 outwardNormal = (rec.p - center) / radius;
    rec.frontFace = dot(ray.direction, outwardNormal) < 0.0f;
    rec.normal = rec.frontFace ? outwardNormal : -outwardNormal;
    return true;
}

// 射线与所有物体求交大管家
bool hitAllSpheres(Ray ray, float tMax, out HitRecord rec) {
    HitRecord tempRec;
    bool hitAnything = false;
    float closestSoFar = tMax;

    // --- 情况 1: 无限水平地面 (定在 Y = -1.8 的高度) ---
    if (ray.direction.y < -0.001f) {
        // 【核心改动】把原来的 -1.8f 直接改成 0.0f！
        float tFloor = (0.0f - ray.origin.y) / ray.direction.y;
        if (tFloor > 0.001f && tFloor < closestSoFar) {
            hitAnything = true;
            closestSoFar = tFloor;
            rec.t = tFloor;
            rec.p = ray.origin + tFloor * ray.direction;
            rec.normal = vec3(0.0f, 1.0f, 0.0f);
            rec.frontFace = true;
            rec.hitType = 1; 
        }
    }

    vec3 dynamicBallAPos = vec3(BALL_A_POS.x, 1.5f + sin(time * 2.5f) * 0.5f, BALL_A_POS.z);
    // --- 情况 2: 1 号球 (樱花玻璃) ---
    if (checkSingleSphere(ray, dynamicBallAPos, BALL_A_R, closestSoFar, tempRec)) {
        hitAnything = true;
        closestSoFar = tempRec.t;
        rec = tempRec;
        rec.hitType = 2; 
    }

    // --- 情况 3: 2 号球 (缎面黄金金属) ---
    if (checkSingleSphere(ray, BALL_B_POS, BALL_B_R, closestSoFar, tempRec)) {
        hitAnything = true;
        closestSoFar = tempRec.t;
        rec = tempRec;
        rec.hitType = 3; 
    }

    // --- 情况 4: 3 号球 (翡翠绿塑料) ---
    if (checkSingleSphere(ray, BALL_C_POS, BALL_C_R, closestSoFar, tempRec)) {
        hitAnything = true;
        closestSoFar = tempRec.t;
        rec = tempRec;
        rec.hitType = 4; 
    }

    return hitAnything;
}

// ======================== 【新加入的独立阴影魔法函数区】 ========================

// 函数 A：2D高空云彩投影函数（梦子捉虫完美解耦版）
float getCloudShadow(vec3 hitPoint, vec3 lightDir, float shaderTime) {
    float cloudHeight = 100.0f;
    float lightY = max(abs(lightDir.y), 0.15f);
    
    // 梦子的物理矢量对齐公式
    vec3 geoOffset = lightDir * ((cloudHeight - hitPoint.y) / lightY);
    vec2 shadowUV = (hitPoint.xz + geoOffset.xz) * 0.05f + vec2(shaderTime * 0.1f, 0.0f);
    
    float cloudDensity = smoothstep(0.4f, 0.7f, fbm(shadowUV));
    // 太阳太低时平滑淡出影子，防止地平线拉扯穿帮
    return mix(1.0f, 0.3f, cloudDensity * smoothstep(0.0f, 0.15f, abs(lightDir.y)));
}

// 函数 B：3D物体遮挡阴影函数（真正的硬核光追阴影！）
vec3 getSphereShadow(vec3 hitPoint, vec3 hitNormal, vec3 lightDir) {
    Ray shadowRay;
    shadowRay.origin = hitPoint + hitNormal * 0.02f; // 保持安全偏移
    shadowRay.direction = lightDir;
    
    // 【核心大重构 1】：因为玻璃球有颜色，我们要把返回的阴影系数从单通道 float 升级成三通道 vec3 颜色因子！
    vec3 shadowColorFactor = vec3(1.0f); // 1.0 代表完全明亮
    
    float k = 16.0f; // 保持丝滑半影柔和度
    
    // 为了方便分流，我们把三个球的配置分开测试
    vec3 centers[3] = vec3[](BALL_A_POS, BALL_B_POS, BALL_C_POS);
    float radii[3] = float[](BALL_A_R, BALL_B_R, BALL_C_R);
    
    // 同步把 1 号球的动态 Y 轴位置拿过来，免得运动时影子穿帮
    centers[0].y = 1.5f + sin(time * 2.5f) * 0.5f;

    for (float t = 0.1f; t < 30.0f; ) {
        vec3 p = shadowRay.origin + t * shadowRay.direction;
        float minSphereDist = 1e20f;
        int hitSphereIdx = -1;
        
        // 找出当前光线位置离哪颗球最近
        for (int i = 0; i < 3; i++) {
            float d = length(p - centers[i]) - radii[i];
            if (d < minSphereDist) {
                minSphereDist = d;
                hitSphereIdx = i;
            }
        }
        
        // 如果光线极度接近或穿透了某颗球
        if (minSphereDist < 0.001f) {
            if (hitSphereIdx == 0) {
                // 【核心净化点 A】：如果阴影被 1 号樱花球挡住了，它绝不变成死黑！
                // 阳光穿过它，会染上一层浪漫空灵的清澈樱花粉色 vec3(1.0f, 0.55f, 0.75f)，让影子彻底变透明！
                shadowColorFactor = min(shadowColorFactor, vec3(1.0f, 0.55f, 0.75f));
            } else {
                // 如果是被不透明的金球或绿球结结实实挡住了，那就维持原判，封顶最黑（0.2f）
                return vec3(0.2f);
            }
        }
        
        // 【核心净化点 B】：半影区过渡计算
        if (hitSphereIdx == 0) {
            // 阳光在玻璃球边缘虚化时，混合温暖的淡粉色过渡
            vec3 edgeGlassColor = mix(vec3(1.0f, 0.75f, 0.85f), vec3(1.0f), clamp(k * minSphereDist / t, 0.0f, 1.0f));
            shadowColorFactor = min(shadowColorFactor, edgeGlassColor);
        } else {
            // 普通不透明球的边缘，照常向死黑（0.2f）做丝滑虚化过渡
            float softEdge = clamp(k * minSphereDist / t, 0.0f, 1.0f);
            shadowColorFactor = min(shadowColorFactor, mix(vec3(0.2f), vec3(1.0f), softEdge));
        }
        
        t += max(minSphereDist, 0.05f);
    }
    
    return shadowColorFactor;
}

// ==============================================================================

float schlickFresnel(float cosine, float refIdx) {
    float r0 = (1.0f - refIdx) / (1.0f + refIdx);
    r0 = r0 * r0;
    return r0 + (1.0f - r0) * pow(1.0f - cosine, 5.0f);
}

vec3 rayColor(Ray camRay, float shaderTime) {
    vec3 finalColor = vec3(0.0f);
    vec3 throughput = vec3(1.0f);
    Ray currentRay = camRay;

    float daySpeed = shaderTime * 0.1f; 
    vec3 sunDir = normalize(vec3(cos(daySpeed) * 1.5f, sin(daySpeed) * 1.5f, 0.5f));
    vec3 moonDir = -sunDir; 

    float isNight = smoothstep(0.1f, -0.1f, sunDir.y); 
    float isDay   = 1.0f - isNight;

    for (int bounce = 0; bounce < 4; ++bounce) {
        HitRecord rec;
        if (!hitAllSpheres(currentRay, 1000.0f, rec)) {
            // -------- 天空盒区域 --------
            vec3 unitDir = normalize(currentRay.direction);
            
            vec3 daySky = mix(vec3(0.6f, 0.8f, 1.0f), vec3(0.05f, 0.35f, 0.9f), max(unitDir.y, 0.0f));
            vec3 sunsetSky = mix(vec3(0.9f, 0.3f, 0.1f), vec3(0.1f, 0.1f, 0.3f), max(unitDir.y, 0.0f));
            vec3 nightSky = mix(vec3(0.02f, 0.02f, 0.05f), vec3(0.0f, 0.0f, 0.02f), max(unitDir.y, 0.0f));
            
            vec3 skyColor = mix(daySky, sunsetSky, smoothstep(0.3f, -0.1f, sunDir.y));
            skyColor = mix(skyColor, nightSky, isNight);

            if (unitDir.y > 0.0f && isNight > 0.0f) {
                float starNoise = hash12(unitDir.xz / (unitDir.y + 0.1f) * 800.0f);
                if (starNoise > 0.996f) {
                    skyColor += vec3(0.9f, 0.95f, 1.0f) * pow(sin(shaderTime * 2.0f + starNoise * 10.0f), 2.0f) * isNight;
                }
            }

            float sunSpot = max(dot(unitDir, sunDir), 0.0f);
            vec3 sun = vec3(1.0f, 0.9f, 0.7f) * pow(sunSpot, 128.0f) * 3.0f * isDay;
            
            float moonSpot = max(dot(unitDir, moonDir), 0.0f);
            vec3 moon = vec3(0.85f, 0.9f, 1.0f) * pow(moonSpot, 256.0f) * 1.5f * isNight;
            skyColor += sun + moon;

            // 高空物理云层渲染
            if (unitDir.y > 0.0f) {
                float cloudHeight = 100.0f; 
                float distToCloud = (cloudHeight - camPos.y) / max(unitDir.y, 1e-4f);
                vec3 skyCloudIntersect = camPos + unitDir * distToCloud;
                float loopedTime = mod(shaderTime, 1000.0f);

                vec2 cloudUV = skyCloudIntersect.xz * 0.012f + vec2(loopedTime * 0.05f, 0.0f); 
                float cloudDensity = smoothstep(0.4f, 0.8f, fbm(cloudUV));
                vec3 cloudColor = mix(vec3(0.95f, 0.96f, 0.98f), vec3(0.2f, 0.23f, 0.3f), isNight);
    
                float horizonFade = smoothstep(0.0f, 1.0f, unitDir.y * 8.0f); 
                horizonFade = pow(horizonFade, 1.8f); 
    
                skyColor = mix(skyColor, cloudColor, cloudDensity * 0.85f * horizonFade);
            }

            finalColor += throughput * skyColor;
            break;
        }

        vec3 activeLightDir = (sunDir.y > 0.0f) ? sunDir : moonDir;
        
        // 【朝霞落日核心魔法 1】：计算当前阳光的基础颜色
        // 当太阳接近地平线（sunDir.y 接近 0 时），通过 mix 强行把阳光染成炽热浪漫的橙红色 vec3(1.0f, 0.35f, 0.1f)！
        float sunsetFactor = smoothstep(0.4f, 0.0f, max(sunDir.y, 0.0f)); 
        vec3 sunColor = mix(vec3(1.0f, 0.9f, 0.7f), vec3(1.0f, 0.35f, 0.1f), sunsetFactor);
        
        // 阳光总强度随高度变化（日落时阳光会变温柔哦）
        float lightIntensity = (sunDir.y > 0.0f) ? mix(1.2f, 0.6f, sunsetFactor) : 0.2f;
        vec3 currentLightColor = (sunDir.y > 0.0f) ? sunColor * lightIntensity : vec3(0.85f, 0.9f, 1.0f) * lightIntensity;

        // 情况 1: 黑白棋盘格地面
        if (rec.hitType == 1) {
            vec3 floorColor = (sin(rec.p.x * 1.5f) * sin(rec.p.z * 1.5f) > 0.0f) ? vec3(0.7f) : vec3(0.0f);
            float diffuseFactor = max(dot(rec.normal, activeLightDir), 0.1f);
            vec3 diffuseLight = floorColor * currentLightColor * diffuseFactor;
            
            float cloudShadowFactor = getCloudShadow(rec.p, activeLightDir, shaderTime); 
            
            // 【核心修复点 2】：地面这里调用时，同样要乖乖传进三个参数！
            vec3 objectShadowFactor = getSphereShadow(rec.p, rec.normal, activeLightDir);
            vec3 finalDiffuseLight = floorColor * currentLightColor * diffuseFactor * cloudShadowFactor * objectShadowFactor;


            // 【经典的纯净太阳光焦散】
            vec3 toBall = rec.p - vec3(BALL_A_POS.x, 1.5f + sin(shaderTime * 2.5f) * 0.5f, BALL_A_POS.z); // 同步动态球位置
            float tOnAxis = dot(-toBall, sunDir);
            vec3 closestPointOnAxis = rec.p + sunDir * tOnAxis;
            float distToAxis = distance(closestPointOnAxis, BALL_A_POS);
            float idealDist = 0.43f * (tOnAxis - 0.5f); 
            float d = abs(distToAxis - idealDist);
            float realCausticIntensity = 0.0f;
            
            if (tOnAxis > 0.0f && sunDir.y > 0.05f) {
                realCausticIntensity += exp(-d * d / 0.004f) * 6.5f / (distToAxis + 0.1f);
                if (distToAxis < idealDist) {
                    realCausticIntensity += pow(1.0f - (distToAxis / idealDist), 2.0f) * 1.5f;
                }
                realCausticIntensity *= smoothstep(0.05f, 0.2f, sunDir.y) * cloudShadowFactor; 
            }
            vec3 sunCausticColor = vec3(2.5f, 0.5f, 0.8f) * realCausticIntensity;

            vec3 totalDiffuseLight = diffuseLight + sunCausticColor;
            finalColor += throughput * totalDiffuseLight;
            
            // 镜面反射
            currentRay.origin = rec.p + rec.normal * 0.001f;
            currentRay.direction = reflect(normalize(currentRay.direction), rec.normal);
            throughput *= 0.15f * cloudShadowFactor * objectShadowFactor; 
        }

        // 情况 2: 樱花粉玻璃球
        if (rec.hitType == 2) {
            float etaiOverEtat = rec.frontFace ? (1.0f / 1.52f) : (1.52f / 1.0f);
            vec3 unitDirection = normalize(currentRay.direction);
            float cosTheta = min(dot(-unitDirection, rec.normal), 1.0f);
            float sinTheta = sqrt(1.0f - cosTheta * cosTheta);
            bool cannotRefract = etaiOverEtat * sinTheta > 1.0f;
            float reflectProb = schlickFresnel(cosTheta, etaiOverEtat);

            vec3 reflectDir = reflect(unitDirection, rec.normal);
            vec3 refractDir = refract(unitDirection, rec.normal, etaiOverEtat);

            if (cannotRefract) {
                currentRay.origin = rec.p + rec.normal * 0.001f;
                currentRay.direction = reflectDir;
            } else {
                float spec = pow(max(dot(reflectDir, activeLightDir), 0.0f), 32.0f);
                finalColor += throughput * vec3(1.0f) * spec * lightIntensity * reflectProb;

                currentRay.origin = rec.p - rec.normal * 0.001f;
                currentRay.direction = refractDir;
                throughput *= (1.0f - reflectProb) * vec3(1.0f, 0.45f, 0.65f); 
            }
        }

        // 情况 3: 缎面黄金金属球
        if (rec.hitType == 3) {
            if (!rec.frontFace) break; 
            vec3 reflectDir = reflect(normalize(currentRay.direction), rec.normal);
            float fuzzySpec = pow(max(dot(reflectDir, activeLightDir), 0.0f), 12.0f);
            
            finalColor += throughput * vec3(1.0f, 0.85f, 0.5f) * (fuzzySpec * 1.5f * lightIntensity); 

            currentRay.origin = rec.p + rec.normal * 0.001f; 
            currentRay.direction = reflectDir;
            throughput *= vec3(1.0f, 0.85f, 0.45f);
        }

        // 情况 4: 翡翠绿塑料球
        if (rec.hitType == 4) {
            if (!rec.frontFace) break;
            
            // 1. 丝滑的 Half-Lambert 漫反射强度计算
            float halfLambert = dot(rec.normal, activeLightDir) * 0.5f + 0.5f;
            float diffuseFactor = pow(halfLambert, 1.5f) * lightIntensity;
            
            // 2. 镜面高光计算
            vec3 reflectDir = reflect(normalize(currentRay.direction), rec.normal);
            float spec = pow(max(dot(reflectDir, activeLightDir), 0.0f), 16.0f) * 0.5f * lightIntensity;
            
            // 3. 【核心修复 1】：把刚刚漏掉的云层阴影计算咒语老老实实补在这里！
            float cloudShadowFactor = getCloudShadow(rec.p, activeLightDir, shaderTime);
            
            // 4. 获取 vec3 类型的多材质彩色软阴影因子
            vec3 objectShadowFactor = getSphereShadow(rec.p, rec.normal, activeLightDir);
            
            // 5. 材质颜色与光影完美融合
            vec3 plasticColor = vec3(0.1f, 0.7f, 0.3f);
            vec3 totalDiffuse = plasticColor * diffuseFactor * cloudShadowFactor * objectShadowFactor;
            vec3 totalSpecular = vec3(1.0f) * spec * cloudShadowFactor * objectShadowFactor;
            
            finalColor += throughput * (totalDiffuse + totalSpecular);
            
            // 【核心修复 2】：优雅地 break 退出追踪，并且只保留这一个干净的大括号闭合，绝不准多留！
            break; 
        }
    }
    return finalColor;
}

void main() {
    vec2 uv = TexCoords * 2.0f - 1.0f;
    uv.x *= aspectRatio;

    vec3 rayDirection = normalize(camFront * 1.0f + camRight * uv.x + camUp * uv.y);

    Ray primaryRay;
    primaryRay.origin = camPos;
    primaryRay.direction = rayDirection;

    vec3 pixelColor = rayColor(primaryRay, time); 
    
    pixelColor = pixelColor / (pixelColor + vec3(1.0f));
    FragColor = vec4(pow(pixelColor, vec3(1.0f / 2.2f)), 1.0f);
}