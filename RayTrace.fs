#version 330 core
out vec4 FragColor;
in vec2 TexCoords;

uniform float aspectRatio;
uniform float time; 
uniform vec3 camPos;
uniform vec3 camFront;
uniform vec3 camRight;
uniform vec3 camUp;

// ================= 魔法配置区：梦子随时可以修改球的位置和半径 =================
const vec3 BALL_A_POS = vec3(0.0f, 0.0f, -5.0f);   // 1号：樱花玻璃球
const float BALL_A_R  = 1.5f;

const vec3 BALL_B_POS = vec3(2.3f, -0.8f, -4.5f);  // 2号：土豪金金属球
const float BALL_B_R  = 0.9f;

const vec3 BALL_C_POS = vec3(-2.2f, -1.0f, -4.0f); // 3号：翡翠绿塑料球
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
        float tFloor = (-1.8f - ray.origin.y) / ray.direction.y;
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

    // --- 情况 2: 1 号球 (樱花玻璃) ---
    if (checkSingleSphere(ray, BALL_A_POS, BALL_A_R, closestSoFar, tempRec)) {
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
    float cloudHeight = 20.0f;
    float lightY = max(abs(lightDir.y), 0.15f);
    
    // 梦子的物理矢量对齐公式
    vec3 geoOffset = lightDir * ((cloudHeight - hitPoint.y) / lightY);
    vec2 shadowUV = (hitPoint.xz + geoOffset.xz) * 0.05f + vec2(shaderTime * 0.1f, 0.0f);
    
    float cloudDensity = smoothstep(0.4f, 0.7f, fbm(shadowUV));
    // 太阳太低时平滑淡出影子，防止地平线拉扯穿帮
    return mix(1.0f, 0.3f, cloudDensity * smoothstep(0.0f, 0.15f, abs(lightDir.y)));
}

// 函数 B：3D物体遮挡阴影函数（真正的硬核光追阴影！）
float getSphereShadow(vec3 hitPoint, vec3 lightDir) {
    Ray shadowRay;
    // 稍微沿着法线偏移一点点（0.001f），防止阴影自相交（自阴影麻点）
    shadowRay.origin = hitPoint + vec3(0.0f, 1.0f, 0.0f) * 0.001f; 
    shadowRay.direction = lightDir;
    
    HitRecord dummyRec;
    // 往光源方向射出一根光线，只测试那三颗3D球体（忽略地面自身）
    // 2号球
    if (checkSingleSphere(shadowRay, BALL_B_POS, BALL_B_R, 1000.0f, dummyRec)) return 0.2f;
    // 3号球
    if (checkSingleSphere(shadowRay, BALL_C_POS, BALL_C_R, 1000.0f, dummyRec)) return 0.2f;
    
    return 1.0f; // 没被遮挡，完全明亮
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

    float daySpeed = shaderTime * 0.001f; 
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
                float cloudHeight = 20.0f;
                vec3 skyCloudIntersect = camPos + unitDir * ((cloudHeight - camPos.y) / unitDir.y);
                vec2 cloudUV = skyCloudIntersect.xz * 0.05f + vec2(shaderTime * 0.1f, 0.0f);
                float cloudDensity = smoothstep(0.4f, 0.8f, fbm(cloudUV));
                vec3 cloudColor = mix(vec3(0.95f, 0.96f, 0.98f), vec3(0.2f, 0.23f, 0.3f), isNight);
                skyColor = mix(skyColor, cloudColor, cloudDensity * 0.85f * smoothstep(0.0f, 0.1f, unitDir.y));
            }

            finalColor += throughput * skyColor;
            break;
        }

        vec3 activeLightDir = (sunDir.y > 0.0f) ? sunDir : moonDir;
        float lightIntensity = (sunDir.y > 0.0f) ? 1.0f : 0.2f;

        // 情况 1: 黑白棋盘格地面（双重阴影融合！）
        if (rec.hitType == 1) {
            vec3 floorColor = (sin(rec.p.x * 1.5f) * sin(rec.p.z * 1.5f) > 0.0f) ? vec3(0.7f) : vec3(0.0f);
            float diffuse = max(dot(rec.normal, activeLightDir), 0.1f) * lightIntensity;
            
            // ─── 【调用分开写的两套阴影魔法】 ───
            float cloudShadowFactor = getCloudShadow(rec.p, activeLightDir, shaderTime); // 2D云影
            float objectShadowFactor = getSphereShadow(rec.p, activeLightDir);           // 3D物体影
            
            // 相互融合叠加到漫反射上
            diffuse *= (cloudShadowFactor * objectShadowFactor);

            // 动态光追拟合焦散（受阴影遮挡影响）
            vec3 toBall = rec.p - BALL_A_POS;
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
            vec3 realCausticColor = vec3(2.5f, 0.5f, 0.8f) * realCausticIntensity;

            finalColor += throughput * (floorColor * diffuse + realCausticColor);
            
            // 镜面反射也附带云层和遮挡的衰减
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
                finalColor += throughput * vec3(1.0f) * spec * reflectProb * 2.0f * lightIntensity;

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
            finalColor += throughput * vec3(1.0f, 0.85f, 0.5f) * fuzzySpec * 1.5f * lightIntensity; 

            currentRay.origin = rec.p + rec.normal * 0.001f; 
            currentRay.direction = reflectDir;
            throughput *= vec3(1.0f, 0.85f, 0.45f);
        }

        // 情况 4: 翡翠绿塑料球
        if (rec.hitType == 4) {
            if (!rec.frontFace) break;
            float diffuse = max(dot(rec.normal, activeLightDir), 0.1f) * lightIntensity;
            vec3 reflectDir = reflect(normalize(currentRay.direction), rec.normal);
            float spec = pow(max(dot(reflectDir, activeLightDir), 0.0f), 16.0f) * 0.5f * lightIntensity;
            
            // 塑料球本身也可以接收高空云影哦！
            float cloudShadowFactor = getCloudShadow(rec.p, activeLightDir, shaderTime);
            diffuse *= cloudShadowFactor;

            finalColor += throughput * (vec3(0.1f, 0.7f, 0.3f) * diffuse + vec3(1.0f) * spec);
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